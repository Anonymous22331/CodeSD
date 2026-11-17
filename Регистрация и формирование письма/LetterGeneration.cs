using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.Data.Metadata;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.ObjectModel.Mapping;
using DocsVision.Platform.ObjectModel.Persistence;
using DocsVision.Platform.SystemCards.ObjectModel.Mapping;
using DocsVision.Platform.SystemCards.ObjectModel.Services;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Mapping;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.Platform.StorageServer.Extensibility;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  sealed class BorrowedSessionProvider : ISessionProvider {public UserSession Session {get;set;}}
  static ObjectContext DocumentContext(UserSession session) {
   var container=new ServiceContainer();container.AddService(typeof(UserSession),session);var context=new ObjectContext(container);
   var mappers=context.GetService<IObjectMapperFactoryRegistry>();mappers.RegisterFactory(typeof(SystemCardsMapperFactory));mappers.RegisterFactory(typeof(BackOfficeMapperFactory));
   var services=context.GetService<IServiceFactoryRegistry>();services.RegisterFactory(typeof(SystemCardsServiceFactory));services.RegisterFactory(typeof(BackOfficeServiceFactory));
   context.AddService<IPersistentStore>(DocsVisionObjectFactory.CreatePersistentStore(new BorrowedSessionProvider{Session=session},null));
   var provider=DocsVisionObjectFactory.CreateMetadataProvider(session);context.AddService<IMetadataManager>(DocsVisionObjectFactory.CreateMetadataManager(provider,session));context.AddService<IMetadataProvider>(provider);return context;
  }
  static string InitialName(StaffEmployee employee) {
   if(employee==null)return "";return ((String.IsNullOrEmpty(employee.FirstName)?"":employee.FirstName.Substring(0,1)+".")+(String.IsNullOrEmpty(employee.MiddleName)?"":employee.MiddleName.Substring(0,1)+".")+" "+employee.LastName).Trim();
  }
  static string GenerationReply(string status,string message) {var xml=new XmlDocument();var e=xml.CreateElement("LetterGenerationResult");xml.AppendChild(e);e.SetAttribute("status",status);e.InnerText=message??"";return xml.OuterXml;}
  [ExtensionMethod] public string GenerateMainLetter(Guid cardId) {
   try {
    var session=CreateSession(true);var card=session.CardManager.GetCardData(cardId);
    if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Ожидается карточка документа.");
    using(var mutex=new Mutex(false,@"Global\AgoraOutgoingLetter-"+cardId.ToString("N"))) {
     bool held=false,ownLock=false;string directory=null;
     try {
      try{held=mutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){held=true;}
      if(!held)throw new InvalidOperationException("Письмо уже формируется. Повторите позднее.");
      if(card.LockStatus==LockStatus.Free){card.PlaceLock();ownLock=true;}
      else if(card.LockStatus!=LockStatus.OwnerLocked)throw new InvalidOperationException("Карточка занята другим пользователем.");
      card.Refresh();var main=card.Sections[Main].FirstRow;var sys=card.Sections[SystemSection].FirstRow;
      if(Id(sys,"Kind")!=Kind || Id(sys,"State")!=PreparationState)throw new InvalidOperationException("Формирование доступно только в подготовке письма АГОРЫ.");
      Guid actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());if(!AuthorOrDeputy(session,Id(main,"Author"),actor))throw new UnauthorizedAccessException("Формировать письмо может только инициатор.");
      if(card.InUpdate)throw new InvalidOperationException("Сначала сохраните карточку.");
      string body=main.GetString("Content");if(String.IsNullOrWhiteSpace(body))throw new InvalidOperationException("Заполните содержание письма.");
      var context=DocumentContext(session);var document=context.GetObject<Document>(cardId);var author=context.GetObject<StaffEmployee>(actor);
      var signerIds=new List<Guid>();foreach(RowData row in card.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows){Guid id=Id(row,"Signer");if(id!=Guid.Empty)signerIds.Add(id);}
      if(signerIds.Count>1)throw new InvalidOperationException("Шаблон рассчитан на одного подписанта.");
      StaffEmployee signer=signerIds.Count==0?null:context.GetObject<StaffEmployee>(signerIds[0]);
      var receivers=new List<Guid>();foreach(RowData row in card.Sections[new Guid("9e5f7ced-2d4a-44c0-8a23-14652e7b373f")].Rows){Guid id=Id(row,"ReceiverPartnerCo");if(id!=Guid.Empty)receivers.Add(id);}
      if(receivers.Count>1)throw new InvalidOperationException("Шаблон рассчитан на одну организацию-получателя.");
      string recipient="";if(receivers.Count==1){var company=context.GetObject<PartnersCompany>(receivers[0]);recipient=String.IsNullOrEmpty(company.FullName)?company.Name:company.FullName;}
      string number="";Guid numberId=Id(main,"RegNumber");if(numberId!=Guid.Empty)number=card.Sections[NumbersSection].Rows[numberId].GetString("Number");
      DateTime? date=main.GetDateTime("RegDate");
      var values=new Dictionary<string,string>{{"РЕГ_ДАТА",date.HasValue?date.Value.ToString("dd MMMM yyyy",CultureInfo.GetCultureInfo("ru-RU"))+" г.":""},{"РЕГ_НОМЕР",number},{"НОМЕР_ДАТА_КОНТРАГЕНТА",IncomingReferenceLine(session,context,document)},{"КОМУ_КОНТРАГЕНТ",recipient},{"ДОЛЖНОСТЬ_ПОДПИСАНТ",signer==null?"":signer.PositionName},{"ФИО_ПОДПИСАНТ",InitialName(signer)},{"ИСПОЛНИТЕЛЬ_ФИО",InitialName(author)},{"ДОБ",author.IPPhone??""}};
      directory=Path.Combine(Path.GetTempPath(),"AgoraOutgoingLetter",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string output=Path.Combine(directory,"Исходящее письмо.docx");
      LetterDocx.Generate(@"C:\AGORA-OUT\templates\outgoing.docx",output,values,body);
      var service=context.GetService<IDocumentService>();var files=document.Files.Where(f=>f.FileType==DocumentFileType.Main).ToArray();
      if(files.Length>0) {
       if(files.Length!=1)throw new InvalidOperationException("В карточке несколько основных файлов. Автоматическая замена запрещена.");
       string existing=service.DownloadMainFile(document,files[0]);
       try{if(LetterDocx.ContentHash(existing)!=LetterDocx.ContentHash(output))throw new InvalidOperationException("Основной файл уже существует и отличается. Он сохранён без изменений.");}
       finally{try{File.Delete(existing);}catch{}}
       return GenerationReply("unchanged","Основной файл уже сформирован.");
      }
      service.AddMainFile(document,output);context.SaveObject(document);context.AcceptChanges();return GenerationReply("attached","Основной файл сформирован.");
     }finally{try{if(ownLock)card.RemoveLock();}finally{if(held)mutex.ReleaseMutex();if(directory!=null){try{Directory.Delete(directory,true);}catch{}}}}
    }
   }catch(Exception ex){Trace("GenerateMainLetter: "+ex.ToString());return GenerationReply("error",ex.Message);}
  }
 }
}
