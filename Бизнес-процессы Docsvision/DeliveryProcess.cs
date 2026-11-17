namespace DVScriptHost {
 public class DVScript {
  public void Execute() {
   var assembly = System.Reflection.Assembly.LoadFrom(@"C:\AGORA-OUT\csharp-runtime\Agora.Outgoing.Workflow.dll");
   try { assembly.GetType("Agora.Outgoing.Workflow.Bootstrap", true).GetMethod("Run").Invoke(null, new object[] { "delivery" }); }
   catch (System.Reflection.TargetInvocationException error) { throw new System.InvalidOperationException("AGORA C# workflow failed", error.InnerException ?? error); }
  }
 }
}