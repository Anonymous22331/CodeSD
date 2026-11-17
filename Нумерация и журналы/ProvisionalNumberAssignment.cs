using System;
using System.IO;
using System.Threading;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectManager.SystemCards;
using DocsVision.Platform.StorageServer.Extensibility;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  static readonly Guid HistorySection=new Guid("e487f7e9-95bd-48cd-a856-c21f8e0ab36c");
  static readonly Guid NumbersSection=new Guid("61c8cc7c-35ce-49e5-9ccd-e9f3c1129445");
  static readonly Guid PreparationState=new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6");
  static Guid NumberingActor(UserSession session,string account) {
   var employees=session.CardManager.GetCardData(new Guid("6710b92a-e148-4363-8a6f-1aa0eb18936c")).Sections[new Guid("dbc8ae9d-c1d2-4d5e-978b-339d22b32482")];
   Guid found=Guid.Empty;
   foreach(RowData employee in employees.GetAllRows()) {
    if(!String.Equals(employee.GetString("AccountName"),account,StringComparison.OrdinalIgnoreCase))continue;
    if(found!=Guid.Empty)throw new UnauthorizedAccessException("Учётной записи соответствуют несколько сотрудников.");
    found=employee.Id;
   }
   if(found==Guid.Empty)throw new UnauthorizedAccessException("Текущий пользователь не зарегистрирован в справочнике сотрудников.");
   return found;
  }
  // The pilot endpoint remains isolated; the draft endpoint uses the business counters.
  [ExtensionMethod] public string AssignProvisionalNumber(Guid cardId) {return AssignNumber(cardId,true);}
  [ExtensionMethod] public string AssignDraftNumber(Guid cardId) {return AssignNumber(cardId,false);}
  string AssignNumber(Guid cardId,bool test) {
   try {
    var config=new XmlDocument();config.XmlResolver=null;config.Load(test?@"C:\AGORA-OUT\server\assignment-test-config.xml":@"C:\AGORA-OUT\server\assignment-config.xml");
    var root=config.DocumentElement;
    if(root==null || root.GetAttribute("mode")!=(test?"test-only":"production"))throw new InvalidOperationException("Присвоение номера ещё не включено.");
    if(test) {
     if(!String.Equals(root.GetAttribute("actorAccount"),Request.Session.UserSid.TranslateToName(),StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Нет прав на тестовое присвоение номера.");
     if(config.SelectSingleNode("/*/Cards/Card[@id='"+cardId.ToString()+"']")==null)throw new InvalidOperationException("Карточка не включена в тестовую нумерацию.");
    }
    var session=CreateSession(true);
    Guid actor=NumberingActor(session,Request.Session.UserSid.TranslateToName());
    var card=session.CardManager.GetCardData(cardId);
    if(card.Type.Id!=DocumentType)throw new InvalidOperationException("Неподдерживаемый тип карточки.");
    using(var mutex=new Mutex(false,@"Global\AgoraOutgoingTempNumber-"+cardId.ToString("N"))) {
     bool held=false;bool ownPlatformLock=false;
     try {
      try{held=mutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){held=true;}
      if(!held)throw new InvalidOperationException("Карточка обрабатывается. Повторите действие позже.");
      var lockStatus=card.LockStatus;
      if(lockStatus==LockStatus.Free){card.PlaceLock();ownPlatformLock=true;}
      else if(lockStatus!=LockStatus.OwnerLocked && lockStatus!=LockStatus.OwnerCheckedOut)throw new InvalidOperationException("Карточка заблокирована другим пользователем.");
      card.Refresh();
      var main=card.Sections[Main].FirstRow;var sys=card.Sections[SystemSection].FirstRow;var extra=card.Sections[Extra].FirstRow;
      if(Id(sys,"Kind")!=Kind || Id(sys,"State")!=PreparationState)throw new InvalidOperationException("Присвоение временного номера допустимо только в подготовке исходящего письма АГОРЫ.");
      if(!AuthorOrDeputy(session,Id(main,"Author"),actor) && !CanEditJournal(DocumentContext(session),actor,Id(extra,"Journal")))throw new UnauthorizedAccessException("Присвоение временного номера доступно инициатору или регистратору журнала.");
      if(extra==null)throw new InvalidOperationException("Не заполнены реквизиты письма.");
      var validation=new XmlDocument();validation.LoadXml(Validate(cardId,false));
      if(validation.DocumentElement.GetAttribute("valid")!="true")throw new InvalidOperationException("Исправьте реквизиты карточки перед присвоением номера.");
      Guid journal=Id(extra,"Journal");Guid oldJournal=Id(extra,"TempNumberJournal");Guid oldRef=Id(main,"RegNumberProvisional");
      var counterNode=config.SelectSingleNode("/*/Counters/Counter[@journal='"+journal.ToString()+"']") as XmlElement;
      if(counterNode==null)throw new InvalidOperationException("Не определён журнал регистрации.");
      var numbers=card.Sections[NumbersSection];string oldText="";
      if(oldRef!=Guid.Empty){if(!numbers.Rows.Contains(oldRef))throw new InvalidOperationException("Нарушена ссылка на временный номер.");oldText=numbers.Rows[oldRef].GetString("Number");if(oldJournal==Guid.Empty)throw new InvalidOperationException("Существующий номер требует проверки происхождения перед перенумерацией.");if(oldJournal==journal)return AssignmentReply("unchanged",oldText,oldRef,journal,extra.GetInt32("TempNumberYear").GetValueOrDefault(),test);}
      var numberManager=ResolveInitiatorManager(DocumentContext(session),Id(main,"Author"),journal);Guid counterId=new Guid(counterNode.GetAttribute("id"));string code=counterNode.GetAttribute("code");
      var counter=(NumeratorCard)session.CardManager.GetCard(counterId);
      if(test ? !counter.Name.StartsWith("АГОРА — ТЕСТ",StringComparison.Ordinal) : counter.Name!="АГОРА — Временный — "+code)throw new InvalidOperationException("Счётчик не соответствует режиму присвоения номера.");
      int year=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time")).Year;
      if(card.InUpdate)throw new InvalidOperationException("Сохраните текущие изменения карточки перед присвоением номера.");
      Guid numericId;int numeric;
      using(var counterMutex=new Mutex(false,@"Global\AgoraOutgoingCounter-"+counterId.ToString("N")+"-"+year)) {
       bool counterHeld=false;
       try {
        try{counterHeld=counterMutex.WaitOne(TimeSpan.FromSeconds(20));}catch(AbandonedMutexException){counterHeld=true;}
        if(!counterHeld)throw new InvalidOperationException("Счётчик занят. Повторите сохранение позднее.");
        string zoneName="Y"+year.ToString(System.Globalization.CultureInfo.InvariantCulture);NumeratorZone zone=null;
        foreach(NumeratorZone existing in counter.Zones)if(existing.Name==zoneName){zone=existing;break;}
        if(zone==null)zone=counter.Zones.AddNew(zoneName);
        numeric=zone.GetNumber(Guid.Empty,true,out numericId);
       }finally{if(counterHeld)counterMutex.ReleaseMutex();}
      }
      string text=Agora.Outgoing.OutgoingNumberFormat.Format(code,true,numeric);Guid newRef;
      card.BeginUpdate();
      try {
       var number=numbers.Rows.AddNew();newRef=number.Id;number.SetGuid("NumericPart",numericId);number.SetString("Number",text);
       var history=card.Sections[HistorySection].Rows.AddNew();history.SetDateTime("ChangedAtUtc",DateTime.UtcNow);history.SetGuid("Actor",actor);history.SetGuid("OldNumberRef",oldRef==Guid.Empty?(Guid?)null:oldRef);history.SetGuid("NewNumberRef",newRef);history.SetString("OldNumber",oldText);history.SetString("NewNumber",text);history.SetGuid("OldJournal",oldJournal==Guid.Empty?(Guid?)null:oldJournal);history.SetGuid("NewJournal",journal);history.SetInt32("Year",year);history.SetString("Reason",oldRef==Guid.Empty?"Первичное присвоение":"Смена журнала");
       extra.SetGuid("InitiatorManager",numberManager.GetObjectId());main.SetGuid("RegNumberProvisional",newRef);extra.SetGuid("TempNumberJournal",journal);extra.SetInt32("TempNumberYear",year);
       card.EndUpdate();
      }catch {try{card.CancelUpdate();}catch{}Trace("Allocated numeric part without completed card mutation: "+numericId);throw;}
      return AssignmentReply(oldRef==Guid.Empty?"assigned":"reissued",text,newRef,journal,year,test);
     }finally {try {if(ownPlatformLock)card.RemoveLock();}finally {if(held)mutex.ReleaseMutex();}}
    }
   }catch(Exception ex){Trace("AssignProvisionalNumber: "+ex.ToString());var xml=new XmlDocument();var e=xml.CreateElement("NumberAssignmentResult");xml.AppendChild(e);e.SetAttribute("status","error");e.SetAttribute("testOnly",test?"true":"false");e.InnerText=ex.Message;return xml.OuterXml;}
  }
  static string AssignmentReply(string status,string number,Guid row,Guid journal,int year,bool test){var xml=new XmlDocument();var e=xml.CreateElement("NumberAssignmentResult");xml.AppendChild(e);e.SetAttribute("status",status);e.SetAttribute("number",number);e.SetAttribute("rowId",row.ToString());e.SetAttribute("journal",journal.ToString());e.SetAttribute("year",year.ToString(System.Globalization.CultureInfo.InvariantCulture));e.SetAttribute("testOnly",test?"true":"false");return xml.OuterXml;}
 }
}