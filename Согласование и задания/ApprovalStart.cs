using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.ObjectModel.Mapping;
using DocsVision.Platform.SystemCards.ObjectModel.Services;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel;
using DocsVision.ApprovalDesigner.ObjectModel.Mapping;
using DocsVision.ApprovalDesigner.ObjectModel.Services;
using DocsVision.Platform.StorageServer.Extensibility;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  static string ApprovalReply(string status,Guid history,string message) {
   var xml=new XmlDocument();var root=xml.CreateElement("ApprovalStartResult");xml.AppendChild(root);root.SetAttribute("status",status);root.SetAttribute("reconcile",history.ToString());root.InnerText=message;return xml.OuterXml;
  }
  [ExtensionMethod] public string StartOutgoingApproval(Guid cardId) {
   Guid historyId=Guid.Empty;
   try {
    var session=CreateSession(true);var card=session.CardManager.GetCardData(cardId);
    if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
    using(var mutex=new Mutex(false,@"Global\AgoraOutgoingApproval-"+cardId.ToString("N"))) {
     bool held=false,ownLock=false;
     try {
      try{held=mutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){held=true;}
      if(!held)throw new InvalidOperationException("Запуск согласования уже выполняется.");
      if(card.LockStatus==LockStatus.Free){card.PlaceLock();ownLock=true;}
      else if(card.LockStatus!=LockStatus.OwnerLocked)throw new InvalidOperationException("Карточка занята другим пользователем.");
      card.Refresh();if(card.InUpdate)throw new InvalidOperationException("Сначала сохраните карточку.");
      var sys=card.Sections[SystemSection].FirstRow;var main=card.Sections[Main].FirstRow;var extra=card.Sections[Extra].FirstRow;
      if(Id(sys,"Kind")!=Kind)throw new InvalidOperationException("Ожидается исходящее письмо АГОРЫ.");
      Guid actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());if(!AuthorOrDeputy(session,Id(main,"Author"),actor))throw new UnauthorizedAccessException("Маршрут запускает инициатор письма.");
      var context=DocumentContext(session);
      context.GetService<IObjectMapperFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerMapperFactory));
      context.GetService<IServiceFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerServiceFactory));
      context.GetService<IObjectMapperFactoryRegistry>().RegisterFactory(typeof(Docsvision.WorkerService.ObjectModel.Mapping.WorkerServiceMapperFactory));
      context.GetService<IServiceFactoryRegistry>().RegisterFactory(typeof(Docsvision.WorkerService.ObjectModel.Services.WorkerServiceServiceFactory));
      historyId=Id(extra,"ActiveApprovalId");
      if(historyId!=Guid.Empty && Id(sys,"State")==PreparationState) {
       var previous=context.GetObject<Reconcile>(historyId);
       if(previous.SystemInfo.State!=null && previous.SystemInfo.State.GetObjectId()==new Guid("6f9d6764-6135-4c05-affa-328a0a749d1c"))historyId=Guid.Empty;
      }
      ApprovalPath path;Reconcile reconcile;
      if(historyId!=Guid.Empty) {
       reconcile=context.GetObject<Reconcile>(historyId);
       if(reconcile.MainInfo.Document==null || reconcile.MainInfo.Document.GetObjectId()!=cardId)throw new InvalidOperationException("Нарушена связь с историей согласования.");
       path=context.GetObject<ApprovalPath>(reconcile.MainInfo.PathId);
       if(path.MainInfo.CurrentCycle!=0 || path.StagesRecords.Count!=0)return ApprovalReply("unchanged",historyId,"Маршрут уже был запущен. Продолжайте работу в заданиях согласования.");
      } else {
       if(Id(sys,"State")!=PreparationState)throw new InvalidOperationException("Запуск доступен только в подготовке.");
       if(extra.GetBoolean("ShortRoute").GetValueOrDefault())throw new InvalidOperationException("Для сокращённого маршрута согласование не запускается.");
       RefreshStoredManager(session,context,card);var validation=new XmlDocument();validation.LoadXml(Validate(cardId,true));
       if(validation.DocumentElement.GetAttribute("valid")!="true")throw new InvalidOperationException(String.Join(Environment.NewLine,validation.SelectNodes("/ValidationResult/Errors/Error").Cast<XmlNode>().Select(n=>n.InnerText).ToArray()));
       if(Id(main,"RegNumberProvisional")==Guid.Empty || Id(extra,"TempNumberJournal")!=Id(extra,"Journal"))throw new InvalidOperationException("Сначала сохраните письмо с временным номером текущего журнала.");
       var group=context.GetObject<StaffGroup>(RegistrarGroup(Id(extra,"Journal")));
       if(!context.GetService<IStaffService>().GetGroupEmployees(group).Any())throw new InvalidOperationException("В группе регистраторов выбранного журнала нет исполнителей.");
       var config=new XmlDocument();config.XmlResolver=null;config.Load(@"C:\AGORA-OUT\server\approval-start-config.xml");
       var configured=config.SelectSingleNode("/ApprovalStartConfig/Path[@journal='"+Id(extra,"Journal")+"']") as XmlElement;
       if(configured==null)throw new InvalidOperationException("Не настроен маршрут выбранного журнала.");
       var pathCard=session.CardManager.GetCardData(new Guid(configured.GetAttribute("id"))).Copy();path=context.GetObject<ApprovalPath>(pathCard.Id);path.MainInfo.Name="АГОРА — "+main.GetString("Name");
       var mapping=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
       bool hasApprovers=card.Sections[new Guid("281a97ff-667f-46c8-8fbe-7cfc02edfedb")].Rows.Cast<RowData>().Any(r=>Id(r,"Approver")!=Guid.Empty);
       Guid signer=card.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows.Cast<RowData>().Select(r=>Id(r,"Signer")).FirstOrDefault(id=>id!=Guid.Empty);
       foreach(var road in path.RoadMaps) {
        Guid oldId=road.Stage.GetObjectId();var copy=session.CardManager.GetCardData(oldId).Copy();var stage=context.GetObject<ApprovalStage>(copy.Id);mapping.Add(oldId.ToString(),copy.Id.ToString());road.Stage=stage;
        if((oldId==new Guid(configured.GetAttribute("manager")) && Id(extra,"InitiatorManager")==signer) || (oldId==new Guid(configured.GetAttribute("additional")) && !hasApprovers)) {road.Excluded=true;stage.MainInfo.DefaultDecision=DecisionSemantics.Positive;}
        context.SaveObject(stage);context.SaveObject(road);
       }
       foreach(var road in path.RoadMaps){if(!String.IsNullOrEmpty(road.Condition)){foreach(var item in mapping){road.Condition=road.Condition.Replace(item.Key,item.Value).Replace(item.Key.ToUpperInvariant(),item.Value.ToUpperInvariant());}}context.SaveObject(road);}context.SaveObject(path);
       var history=session.CardManager.CreateCardData(context.GetObjectTypeId(typeof(Reconcile)));reconcile=context.GetObject<Reconcile>(history.Id);var employee=context.GetObject<StaffEmployee>(actor);
       reconcile.MainInfo.Title=main.GetString("Name");reconcile.MainInfo.Author=employee;reconcile.MainInfo.Initiator=context.GetObject<StaffEmployee>(Id(main,"Author"));reconcile.MainInfo.Consolidator=reconcile.MainInfo.Initiator;reconcile.MainInfo.Document=context.GetObject<Document>(cardId);reconcile.MainInfo.PathId=path.GetObjectId();reconcile.MainInfo.CreatedPathId=path.GetObjectId();
       var tasks=context.GetService<ITaskListService>().CreateTaskList();context.SaveObject(tasks);reconcile.MainInfo.TaskList=tasks;
       var files=context.GetService<IFileListService>().CreateFileList();context.SaveObject(files);reconcile.MainInfo.ReconcileFileList=files;
       reconcile.SystemInfo.CardKind=context.GetObject<KindsCardKind>(new Guid("3c3c0aeb-1b24-49c2-8a05-7f49cb46d481"));reconcile.SystemInfo.State=context.GetObject<StatesState>(new Guid("3dd7a060-0c3b-421b-baaa-62fd4e83cc99"));context.SaveObject(reconcile);
       context.GetService<IApprovalService>().CopyNewFilesFromDocument(reconcile.MainInfo.Document,reconcile,0,"Исходное письмо перед согласованием");context.SaveObject(files);context.SaveObject(reconcile);
       if(files.Files.Count==0)throw new InvalidOperationException("Основной файл не перенесён в согласование.");
       // Persist the checkpoint before native StartPath can enqueue any task.
       historyId=history.Id;extra.SetGuid("ActiveApprovalId",historyId);
       var link=card.Sections[new Guid("e3e14386-27a4-4d24-96ce-9907cdcc438b")].FirstRow;
       if(link==null)link=card.Sections[new Guid("e3e14386-27a4-4d24-96ce-9907cdcc438b")].Rows.AddNew();link.SetGuid("ReconciliationRoute",historyId);
      }
      // A stored checkpoint with no native cycle can be resumed without creating another history.
      if(Id(sys,"State")!=PreparationState)throw new InvalidOperationException("Незавершённый запуск требует восстановления администратором.");
      context.GetService<IApprovalPathService>().StartPath(path,reconcile);context.SaveObject(path);context.SaveObject(reconcile);
      var events=context.GetService<IEventService>();events.FlushEvents(path,true);events.FlushEvents(reconcile,true);foreach(var record in path.StagesRecords)events.FlushEvents(record.Stage,true);
      TriggerAccessRefresh();Trace("StartOutgoingApproval card="+cardId+" reconcile="+historyId+" actor="+actor);return ApprovalReply("started",historyId,"Маршрут согласования запущен.");
     }finally{try{if(ownLock)card.RemoveLock();}finally{if(held)mutex.ReleaseMutex();}}
    }
   }catch(Exception ex){Trace("StartOutgoingApproval: "+ex.ToString());return ApprovalReply("error",historyId,ex.Message);}
  }
 }
}
