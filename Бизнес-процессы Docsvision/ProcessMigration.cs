using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using DocsVision.Workflow.Objects;
using DocsVision.Workflow.Functions;
using Agora.Outgoing.Deployment;
namespace Agora.Outgoing.Workflow {
 public static class ProcessMigration {
  static IEnumerable<dynamic> Values(object collection){return ((IEnumerable)collection.GetType().GetProperty("Values").GetValue(collection,null)).Cast<object>().Select(x=>(dynamic)x);}
  public static void Apply(bool auditOnly){Bootstrap.Initialize();ContextConnection.Open("dv002_exam");Library library=null;try{library=new Library(ContextConnection.Session);var manifest=Runtime.Read<Dictionary<string,object>>(@"C:\AGORA-OUT\workflow-runtime\processes.json");var reports=new List<object>();string backup=Path.Combine(Runtime.Root,"backups","csharp-workflow-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));if(!auditOnly)Directory.CreateDirectory(backup);
   foreach(string mode in new[]{"Delivery","Archive"})foreach(string role in new[]{"template","instance"}){var entry=(Dictionary<string,object>)manifest[mode];var process=library.GetProcess(new Guid((string)entry[role]));var scripts=Values(process.Functions).Where(f=>((string)f.Data??"").Contains("CodeSource")).ToArray();if(scripts.Length!=1)throw new InvalidOperationException("Expected one C# function: "+mode+" "+role);dynamic body=scripts[0];string old=body.Data;bool wasActive=role=="instance"&&process.State==ProcessStateEnum.Active;
    if(!auditOnly){File.WriteAllText(Path.Combine(backup,mode+"-"+role+"-script.xml"),old);if(wasActive)process.Pause(Environment.MachineName+"\\Administrator");try{string code=File.ReadAllText(Path.Combine(Runtime.Root,@"csharp-runtime\"+mode+"Process.cs"));var xml=new XmlDocument();var root=xml.CreateElement("Settings");xml.AppendChild(root);foreach(var pair in new[]{new[]{"CSharp","True"},new[]{"CodeSource",code}}){var el=xml.CreateElement(pair[0]);el.InnerText=pair[1];root.AppendChild(el);}root.AppendChild(xml.CreateElement("Files"));var checker=new ScriptFunction();checker.FunctionData=body;checker.Data=xml.OuterXml;checker.CheckCompile();process.BeginUpdate();try{body.Data=xml.OuterXml;foreach(dynamic state in body.States)state.Data=xml.OuterXml;process.EndUpdate();}catch{process.CancelUpdate();throw;}if(role=="template"){process.Prepare();process.BeginUpdate();process.TemplateState=TemplateStateEnum.InUse;process.EndUpdate();}}
     finally{if(wasActive)process.Resume(Environment.MachineName+"\\Administrator",library.Dictionary);}}
    var execution=new List<object>();foreach(dynamic function in Values(process.Functions)){var passes=new List<object>();foreach(dynamic pass in function.States)passes.Add(new {state=pass.State.ToString(),hasErrors=(bool)pass.HasErrors,executeTime=((DateTime)pass.ExecuteTime).ToString("o")});execution.Add(new {caption=(string)function.Caption,states=passes});}string data=(string)body.Data;reports.Add(new {execution=execution,mode=mode,role=role,id=process.ID.ToString(),state=process.State.ToString(),containsPowerShell=data.IndexOf("powershell",StringComparison.OrdinalIgnoreCase)>=0||data.Contains(".ps1"),code=data});}
   Runtime.Save(Path.Combine(Runtime.Root,"csharp-runtime",auditOnly?"process-audit.json":"process-migration.json"),new {utc=DateTime.UtcNow.ToString("o"),processes=reports,backup=auditOnly?null:backup});
  }finally{if(library!=null)library.Shutdown();ContextConnection.Close();}}
 }
}
