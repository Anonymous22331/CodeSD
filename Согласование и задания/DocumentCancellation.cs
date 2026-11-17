using System;
using System.Linq;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  [ExtensionMethod] public string CancelOutgoingDocument(Guid cardId) {
   CardData raw=null;bool locked=false;
   try {
    var session=CreateSession(true);var c=DocumentContext(session);var document=OwnOutgoing(session,c,cardId);raw=session.CardManager.GetCardData(cardId);
    if(raw.LockStatus==LockStatus.Free){raw.PlaceLock();locked=true;}else if(raw.LockStatus!=LockStatus.OwnerLocked && raw.LockStatus!=LockStatus.OwnerCheckedOut)throw new InvalidOperationException("Документ занят другим пользователем.");
    raw.Refresh();if(raw.InUpdate)throw new InvalidOperationException("Сохраните изменения документа.");
    var actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());
    if(!AuthorOrDeputy(session,Id(raw.Sections[Main].FirstRow,"Author"),actor))throw new UnauthorizedAccessException("Отменить подготовку письма может инициатор или его заместитель.");
    Guid state=Id(raw.Sections[SystemSection].FirstRow,"State"),cancelled=new Guid("d1a561ba-e16e-4042-afa8-7366c0ccfba9");
    if(state==cancelled)return SendingReply("unchanged","Письмо уже отменено.");
    if(state!=PreparationState)throw new InvalidOperationException("Сначала отмените согласование и верните документ в подготовку.");
    var service=c.GetService<IStateService>();var settings=service.GetKindStateSetting(c.GetObject<KindsCardKind>(Kind));
    var branch=settings.StateMachineBranches.Single(b=>b.StartState.GetObjectId()==PreparationState && b.EndState.GetObjectId()==cancelled && b.Operation.DefaultName=="AGORA.Document.Cancel");
    if(!service.IsOperationAllowedFull(branch.Operation,document))throw new UnauthorizedAccessException("Операция отмены недоступна.");
    c.RefreshObject(document);service.ChangeState(document,branch);c.SaveObject(document);c.AcceptChanges();TriggerAccessRefresh();
    Trace("CancelOutgoingDocument card="+cardId+" actor="+actor);return SendingReply("cancelled","Письмо отменено.");
   }catch(Exception ex){Trace("CancelOutgoingDocument: "+ex);return SendingReply("error",ex.Message);}finally{if(locked && raw!=null)raw.RemoveLock();}
  }
 }
}
