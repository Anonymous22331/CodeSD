using System;
using System.Linq;
using System.Xml;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.Platform.ObjectManager;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  [ExtensionMethod] public string BeginShortRegistration(Guid cardId) {
   CardData card=null;bool ownLock=false;
   try {
    var session=CreateSession(true);card=session.CardManager.GetCardData(cardId);if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
    if(card.LockStatus==LockStatus.Free){card.PlaceLock();ownLock=true;}else if(card.LockStatus!=LockStatus.OwnerLocked)throw new InvalidOperationException("Карточка занята другим пользователем.");card.Refresh();if(card.InUpdate)throw new InvalidOperationException("Сначала сохраните карточку.");
    var main=card.Sections[Main].FirstRow;var sys=card.Sections[SystemSection].FirstRow;var extra=card.Sections[Extra].FirstRow;
    if(Id(sys,"Kind")!=Kind || Id(sys,"State")!=PreparationState || !extra.GetBoolean("ShortRoute").GetValueOrDefault())throw new InvalidOperationException("Сокращённая регистрация доступна в подготовке при выбранном сокращённом маршруте.");
    Guid actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());if(Id(main,"Author")!=actor)throw new UnauthorizedAccessException("Сокращённый маршрут запускает инициатор письма.");
    var c=DocumentContext(session);var staff=c.GetService<IStaffService>();if(!staff.IsEmployeeInGroup(c.GetObject<StaffEmployee>(actor),c.GetObject<StaffGroup>(RegistrarGroup(Id(extra,"Journal"))),true))throw new UnauthorizedAccessException("Инициатор должен быть регистратором выбранного журнала.");
    if(Empty(main,"Name") || Empty(main,"Content") || Id(main,"RegistrationPlaceId")==Guid.Empty || Id(extra,"DocumentType")==Guid.Empty)throw new InvalidOperationException("Заполните тему, содержание, тип и место регистрации письма.");
    if(!card.Sections[new Guid("9e5f7ced-2d4a-44c0-8a23-14652e7b373f")].Rows.Cast<RowData>().Any(r=>Id(r,"ReceiverPartnerCo")!=Guid.Empty))throw new InvalidOperationException("Укажите организацию-получателя.");
    var validation=new XmlDocument();validation.LoadXml(Validate(cardId,false));if(validation.DocumentElement.GetAttribute("valid")!="true")throw new InvalidOperationException("Исправьте реквизиты письма.");
    var proof=new XmlDocument();proof.LoadXml(ConfirmSignature(cardId,true));if(proof.DocumentElement.GetAttribute("status")!="verified")throw new InvalidOperationException(proof.DocumentElement.InnerText);
    extra.SetGuid("ShortRouteConfirmedBy",actor);extra.SetGuid("ShortRouteConfirmedSignature",new Guid(proof.DocumentElement.GetAttribute("signatureId")));
    var document=c.GetObject<Document>(cardId);var service=c.GetService<IStateService>();var setting=service.GetKindStateSetting(c.GetObject<KindsCardKind>(Kind));var branches=setting.StateMachineBranches.Where(b=>b.StartState.GetObjectId()==PreparationState && b.EndState.GetObjectId()==new Guid("d1754352-3891-46fa-9fd5-61cee8844443") && b.Operation.DefaultName=="AGORA.ShortRoute.Verified").ToArray();if(branches.Length!=1)throw new InvalidOperationException("Не настроен сокращённый переход регистрации.");service.ChangeState(document,branches[0]);c.SaveObject(document);c.AcceptChanges();TriggerAccessRefresh();Trace("BeginShortRegistration card="+cardId+" actor="+actor);return SendingReply("queued","Письмо с проверенной ЭП направлено на регистрацию.");
   }catch(Exception ex){Trace("BeginShortRegistration: "+ex.ToString());return SendingReply("error",ex.Message);}
   finally{if(ownLock && card!=null)card.RemoveLock();}
  }
 }
}
