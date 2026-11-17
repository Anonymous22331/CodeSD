using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.BackOffice.ObjectModel.Services.Entities.KindSetting;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  // Verification prerequisite for registration. This never changes the document state or signs a file.
  [ExtensionMethod] public string ConfirmOutgoingSignature(Guid cardId) {return ConfirmSignature(cardId,false);}
  string ConfirmSignature(Guid cardId,bool preparationAllowed) {
   bool ownLock=false;CardData card=null;
   var xml=new XmlDocument();var reply=xml.CreateElement("SignatureConfirmationResult");xml.AppendChild(reply);reply.SetAttribute("status","error");
   try {
    var session=CreateSession(true);card=session.CardManager.GetCardData(cardId);
    if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
    if(card.LockStatus==LockStatus.Free){card.PlaceLock();ownLock=true;}
    else if(card.LockStatus!=LockStatus.OwnerLocked)throw new InvalidOperationException("Карточка занята другим пользователем.");
    card.Refresh();if(card.InUpdate)throw new InvalidOperationException("Сначала сохраните карточку.");
    var sys=card.Sections[SystemSection].FirstRow;var extra=card.Sections[Extra].FirstRow;var main=card.Sections[Main].FirstRow;
    if(Id(sys,"Kind")!=Kind || (Id(sys,"State")!=new Guid("d1754352-3891-46fa-9fd5-61cee8844443") && !(preparationAllowed && Id(sys,"State")==PreparationState && extra.GetBoolean("ShortRoute").GetValueOrDefault())))throw new InvalidOperationException("Проверка подписи недоступна в текущем состоянии.");
    var declared=card.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows.Cast<RowData>().Select(r=>Id(r,"Signer")).Where(id=>id!=Guid.Empty).ToArray();
    if(declared.Length!=1)throw new InvalidOperationException("В письме должен быть один подписант.");
    var context=DocumentContext(session);Guid actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());
    if(!AuthorizedDeputy(context,declared[0],actor,true) && !AuthorOrDeputy(session,Id(main,"Author"),actor) && !RegistrarOrDeputy(context,actor,RegistrarGroup(Id(extra,"Journal"))) && !RegistrationAutomation())throw new UnauthorizedAccessException("Нет прав на подтверждение подписи этого письма.");
    var document=context.GetObject<Document>(cardId);var files=document.Files.Where(f=>f.FileType==DocumentFileType.Main).ToArray();
    if(files.Length!=1)throw new InvalidOperationException("Требуется один подписанный основной файл.");
    if(files[0].FileVersionId==Guid.Empty)throw new InvalidOperationException("Не определена версия подписываемого файла.");
    if(document.MainInfo.SignatureList==null)throw new InvalidOperationException("Криптографическая подпись основного файла отсутствует.");
    var service=context.GetService<IDocumentService>();
    foreach(var signature in document.MainInfo.SignatureList.Signatures.OrderByDescending(s=>s.TimeStamp)) {
     if(signature.Signer==null || !AuthorizedDeputy(context,declared[0],signature.Signer.GetObjectId(),true) || String.IsNullOrWhiteSpace(signature.Certificate))continue;
     if(!signature.Parts.Any(p=>service.IsDocumentSignaturePartMainFileWithContent(p) && p.SignatureType!=SignatureType.Hash && p.SignatureType!=SignatureType.Unknown))continue;
     if(!service.CheckAllMainFilesSigned(document,signature))continue;
     try {
      using(var certificate=new X509Certificate2(Convert.FromBase64String(signature.Certificate))) {
       var result=service.VerifySignature(document,signature,certificate,true,false,new List<CardFieldSetting>());
       if(!result.IsValid || !result.IsValidCertificate || result.IsNotAllMainFilesSigned || result.IsExpiredCertificate || result.IsRevokedCertificate)continue;
       DateTime? signedTime=null;
       foreach(var part in signature.Parts.Where(p=>service.IsDocumentSignaturePartMainFileWithContent(p))){var time=service.GetSignatureTimestamp(document,part,true,false,new CardFieldSetting[0]);if(time.HasValue){signedTime=time;break;}}
       if((preparationAllowed || extra.GetBoolean("ShortRoute").GetValueOrDefault()) && !signedTime.HasValue)throw new InvalidOperationException("Для сокращённого маршрута подпись должна содержать время подписания. Текущая дата вместо него не подставляется.");
       // Audit values are populated only after native verification of the current file contents.
       card.BeginUpdate();
       try{extra.SetGuid("ActualSigner",signature.Signer.GetObjectId());extra.SetGuid("SignedFileVersion",files[0].FileVersionId);extra.SetGuid("VerifiedSignatureId",signature.GetObjectId());extra.SetString("SigningCertificateThumbprint",certificate.Thumbprint);card.EndUpdate();}
       catch{card.CancelUpdate();throw;}
       reply.SetAttribute("status","verified");reply.SetAttribute("signatureId",signature.GetObjectId().ToString());reply.SetAttribute("cardId",cardId.ToString());reply.SetAttribute("signer",signature.Signer.GetObjectId().ToString());reply.SetAttribute("signerName",(signature.Signer.LastName+" "+signature.Signer.FirstName+" "+signature.Signer.MiddleName).Trim());reply.SetAttribute("signatureTime",signedTime.GetValueOrDefault(signature.TimeStamp).ToString("o"));reply.SetAttribute("fileVersion",files[0].FileVersionId.ToString());reply.SetAttribute("certificate",certificate.Thumbprint);reply.InnerText="Криптографическая подпись текущей версии основного файла подтверждена.";
       Trace("ConfirmOutgoingSignature card="+cardId+" signature="+signature.GetObjectId()+" actor="+actor);return xml.OuterXml;
      }
     }catch(System.Security.Cryptography.CryptographicException){continue;}catch(FormatException){continue;}
    }
    throw new InvalidOperationException("Нет действительной криптографической подписи назначенного подписанта для текущей версии файла. Регистрация заблокирована.");
   }catch(Exception ex){Trace("ConfirmOutgoingSignature: "+ex.ToString());reply.InnerText=ex.Message;return xml.OuterXml;}
   finally{if(ownLock && card!=null)card.RemoveLock();}
  }
 }
}
