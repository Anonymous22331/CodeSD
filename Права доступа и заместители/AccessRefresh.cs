using System;
using System.Reflection;
using System.Runtime.InteropServices;
using DocsVision.Platform.StorageServer.Extensibility;
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension {
  void TriggerAccessRefresh() {
   object scheduler=null,folder=null,task=null;
   try {
    scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));scheduler.GetType().InvokeMember("Connect",BindingFlags.InvokeMethod,null,scheduler,new object[0]);folder=scheduler.GetType().InvokeMember("GetFolder",BindingFlags.InvokeMethod,null,scheduler,new object[]{"\\"});task=folder.GetType().InvokeMember("GetTask",BindingFlags.InvokeMethod,null,folder,new object[]{"AGORA-OUT AccessPolicy"});task.GetType().InvokeMember("Run",BindingFlags.InvokeMethod,null,task,new object[]{null});
   }catch(Exception ex){Trace("Access policy refresh will be retried by schedule: "+ex.Message);}
   finally{foreach(var value in new[]{task,folder,scheduler})if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
  }
  [ExtensionMethod] public string RequestOutgoingAccessRefresh(Guid cardId) {
   try{var card=CreateSession(true).CardManager.GetCardData(cardId);if(card.Type.Id!=DocumentType || Id(card.Sections[SystemSection].FirstRow,"Kind")!=Kind)throw new InvalidOperationException("Неподдерживаемый вид документа.");TriggerAccessRefresh();return SendingReply("queued","Права обновляются.");}catch(Exception ex){Trace("RequestOutgoingAccessRefresh: "+ex.Message);return SendingReply("error",ex.Message);}
  }
 }
}
