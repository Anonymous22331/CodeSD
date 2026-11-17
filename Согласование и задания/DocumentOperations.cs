using System;
using System.Linq;
using System.Xml;
using System.Net.Mail;
using System.Text;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.StorageServer.Extensibility;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  static StatesOperation DocumentOperation(ObjectContext c,Document d,int row) {
   var config=new XmlDocument();config.XmlResolver=null;config.Load(@"C:\AGORA-OUT\server\document-operations.xml");var entry=config.SelectSingleNode("/DocumentOperations/Operation[@row='"+row+"']") as XmlElement;if(entry==null)throw new InvalidOperationException("Операция ещё не настроена.");
   var operation=c.GetObject<StatesOperation>(new Guid(entry.GetAttribute("id")));if(!c.GetService<IStateService>().IsOperationAllowedFull(operation,d))throw new UnauthorizedAccessException("Операция недоступна для вашей роли и состояния письма.");return operation;
  }
  Document OwnOutgoing(UserSession s,ObjectContext c,Guid cardId){var raw=s.CardManager.GetCardData(cardId);if(raw.Type.Id!=DocumentType || Id(raw.Sections[SystemSection].FirstRow,"Kind")!=Kind)throw new InvalidOperationException("Неподдерживаемый документ.");return c.GetObject<Document>(cardId);}
  [ExtensionMethod] public string GetOutgoingDocumentOperations(Guid cardId) {
   var xml=new XmlDocument();var root=xml.CreateElement("DocumentOperations");xml.AppendChild(root);root.SetAttribute("status","ok");
   try{var session=CreateSession(true);var c=DocumentContext(session);var d=OwnOutgoing(session,c,cardId);foreach(int row in new[]{27,28,35}){try{var op=DocumentOperation(c,d,row);var item=xml.CreateElement("Operation");item.SetAttribute("row",row.ToString());item.SetAttribute("name",op.LocalizedName);root.AppendChild(item);}catch(UnauthorizedAccessException){}}}catch(Exception e){root.SetAttribute("status","error");root.InnerText=e.Message;}return xml.OuterXml;
  }
  [ExtensionMethod] public string GetOutgoingApprovalHistory(Guid cardId) {
   try{var session=CreateSession(true);var c=DocumentContext(session);OwnOutgoing(session,c,cardId);DocumentOperation(c,c.GetObject<Document>(cardId),35);var raw=session.CardManager.GetCardData(cardId);var history=Id(raw.Sections[Extra].FirstRow,"ActiveApprovalId");if(history==Guid.Empty)throw new InvalidOperationException("Согласование этого письма ещё не запускалось.");session.CardManager.GetCardData(history);return ApprovalReply("open",history,"Ход согласования");}catch(Exception e){return ApprovalReply("error",Guid.Empty,e.Message);}
  }
  static void RegisterTrustedFamiliarization(Guid document,Guid task,Guid author) {
   using(System.Security.Principal.WindowsIdentity.Impersonate(IntPtr.Zero)){
    string directory=@"C:\ProgramData\Docsvision\AGORA-OUT\trusted-auxiliary";if(!System.IO.Directory.Exists(directory))throw new InvalidOperationException("Не настроен реестр заданий ознакомления.");
    var xml=new XmlDocument();var root=xml.CreateElement("TrustedAuxiliaryTask");xml.AppendChild(root);root.SetAttribute("document",document.ToString());root.SetAttribute("task",task.ToString());root.SetAttribute("author",author.ToString());root.SetAttribute("kind","e3f68b8d-24b8-468d-9fd0-ab4d387948f8");root.SetAttribute("createdUtc",DateTime.UtcNow.ToString("o"));
    string path=System.IO.Path.Combine(directory,task.ToString("N")+".xml"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";xml.Save(temporary);System.IO.File.Move(temporary,path);
   }
  }
  [ExtensionMethod] public string CreateOutgoingFamiliarizationDraft(Guid cardId) {
   try{var session=CreateSession(true);var c=ApprovalContext(session);var d=OwnOutgoing(session,c,cardId);DocumentOperation(c,d,27);var kind=c.GetObject<KindsCardKind>(new Guid("e3f68b8d-24b8-468d-9fd0-ab4d387948f8"));if(kind.Name!="На ознакомление")throw new InvalidOperationException("Не найден штатный вид задания на ознакомление.");var task=c.GetService<ITaskService>().CreateTask(kind);task.MainInfo.Name="Ознакомиться: "+d.MainInfo.Name;task.MainInfo.Author=c.GetService<IStaffService>().GetCurrentEmployee();task.MainInfo.ParentDocument=d;c.SaveObject(task);c.AcceptChanges();RegisterTrustedFamiliarization(cardId,task.GetObjectId(),task.MainInfo.Author.GetObjectId());TriggerAccessRefresh();Trace("Familiarization draft="+task.GetObjectId()+" document="+cardId);return ApprovalReply("draft",task.GetObjectId(),"Укажите исполнителей и отправьте штатное задание.");}catch(Exception e){Trace("Familiarization: "+e);return ApprovalReply("error",Guid.Empty,e.Message);}
  }
  [ExtensionMethod] public string SendOutgoingDocumentMessage(Guid cardId,string recipientEmail,string message) {
   try{if(String.IsNullOrWhiteSpace(message) || message.Length>20000)throw new InvalidOperationException("Введите сообщение длиной до 20 000 символов.");var recipient=new MailAddress(recipientEmail);if(!recipient.Address.EndsWith("@agora.test",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Тестовая почта принимает адреса только в домене agora.test.");var session=CreateSession(true);var c=DocumentContext(session);var d=OwnOutgoing(session,c,cardId);DocumentOperation(c,d,28);var config=new XmlDocument();config.XmlResolver=null;config.Load(@"C:\AGORA-OUT\server\test-smtp.xml");var r=config.DocumentElement;if(r.GetAttribute("host")!="127.0.0.1")throw new InvalidOperationException("Не настроен тестовый SMTP.");using(var mail=new MailMessage(r.GetAttribute("sender"),recipient.Address))using(var smtp=new SmtpClient("127.0.0.1",Int32.Parse(r.GetAttribute("port")))){smtp.Timeout=30000;mail.Subject="Сообщение по документу: "+d.MainInfo.Name;mail.SubjectEncoding=Encoding.UTF8;mail.BodyEncoding=Encoding.UTF8;mail.Body=message+Environment.NewLine+Environment.NewLine+"Автор: "+Request.Session.UserAccount+Environment.NewLine+"http://192.168.10.134:8085/#/Card/"+cardId;smtp.Send(mail);}return SendingReply("sent","Сообщение передано тестовому SMTP.");}catch(Exception e){Trace("Document message: "+e);return SendingReply("error",e.Message);}
  }
 }
}
