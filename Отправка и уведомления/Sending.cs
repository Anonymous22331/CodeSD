using System;
using System.Linq;
using System.Threading;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  static readonly Guid RegisteredState=new Guid("11b0eb5d-3fa1-48a9-ba0b-e9ed1c2b1510");
  static readonly Guid SentState=new Guid("75fe7ede-fbaa-4c32-b3eb-16e1e7b99b9f");
  static Guid RegistrarGroup(Guid journal) {
   if(journal==new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e"))return new Guid("4adf3c4e-aae8-466e-be68-91eba2c2dc89");
   if(journal==new Guid("08137765-5066-4e5d-a189-4f459e32feab"))return new Guid("6dc14225-3c84-49f9-aa26-4f4ea5ce9755");
   if(journal==new Guid("adc70c7d-54d7-4a13-aa6f-2cc208f572b7"))return new Guid("bd966761-ac24-4de3-a6c6-23032b8a6b39");
   throw new InvalidOperationException("Не определён журнал регистрации.");
  }
  static string SendingReply(string status,string message) {
   var xml=new XmlDocument();var root=xml.CreateElement("SendingResult");xml.AppendChild(root);root.SetAttribute("status",status);root.InnerText=message;return xml.OuterXml;
  }
  // Records an external delivery. No email is sent by this operation.
  [ExtensionMethod] public string MarkOutgoingSent(Guid cardId) {
   try {
    var session=CreateSession(true);var card=session.CardManager.GetCardData(cardId);
    if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
    using(var mutex=new Mutex(false,@"Global\AgoraOutgoingSending-"+cardId.ToString("N"))) {
     bool held=false,ownLock=false;
     try {
      try{held=mutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){held=true;}
      if(!held)throw new InvalidOperationException("Операция уже выполняется. Повторите позднее.");
      if(card.LockStatus==LockStatus.Free){card.PlaceLock();ownLock=true;}
      else if(card.LockStatus!=LockStatus.OwnerLocked)throw new InvalidOperationException("Карточка занята другим пользователем.");
      card.Refresh();if(card.InUpdate)throw new InvalidOperationException("Сначала сохраните карточку.");
      var sys=card.Sections[SystemSection].FirstRow;var main=card.Sections[Main].FirstRow;var extra=card.Sections[Extra].FirstRow;
      if(Id(sys,"Kind")!=Kind)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
      var actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());var context=DocumentContext(session);
      Guid groupId=RegistrarGroup(Id(extra,"Journal"));
      if(!AuthorOrDeputy(session,Id(main,"Author"),actor)) {
       var employee=context.GetObject<StaffEmployee>(actor);var group=context.GetObject<StaffGroup>(groupId);
       if(!RegistrarOrDeputy(context,actor,group.GetObjectId()))throw new UnauthorizedAccessException("Отметить отправку может инициатор или регистратор выбранного журнала.");
      }
      if(Id(sys,"State")==SentState)return SendingReply("unchanged","Письмо уже отмечено отправленным.");
      if(Id(sys,"State")!=RegisteredState)throw new InvalidOperationException("Отправку можно отметить только в состоянии «Зарегистрирован».");
      if(Id(extra,"DeliveryMethod")==Guid.Empty)throw new InvalidOperationException("Укажите способ отправки.");
      var check=new Result(cardId,false);Reference(session,extra,"DeliveryMethod",new Guid("39df3f1e-aeb5-4aad-b80d-437b9125aaf0"),"Способ отправки",check);
      var validation=new XmlDocument();validation.LoadXml(check.ToString());if(validation.DocumentElement.GetAttribute("valid")!="true")throw new InvalidOperationException("Выберите способ отправки из справочника АГОРЫ.");
      // The optional actual sending date is retained exactly as entered; never default it to today.
      var document=context.GetObject<Document>(cardId);var service=context.GetService<IStateService>();var setting=service.GetKindStateSetting(context.GetObject<KindsCardKind>(Kind));
      var branches=setting.StateMachineBranches.Where(b=>b.StartState!=null && b.EndState!=null && b.Operation!=null && b.StartState.GetObjectId()==RegisteredState && b.EndState.GetObjectId()==SentState && b.Operation.DefaultName=="AGORA.Delivery.MarkSent").ToArray();
      if(branches.Length!=1)throw new InvalidOperationException("Не настроен переход отметки отправки.");
      service.ChangeState(document,branches[0]);context.SaveObject(document);context.AcceptChanges();
      TriggerAccessRefresh();Trace("MarkOutgoingSent card="+cardId+" actor="+actor);return SendingReply("sent","Письмо отмечено отправленным.");
     }finally{try{if(ownLock)card.RemoveLock();}finally{if(held)mutex.ReleaseMutex();}}
    }
   }catch(Exception ex){Trace("MarkOutgoingSent: "+ex.ToString());return SendingReply("error",ex.Message);}
  }
 }
}
