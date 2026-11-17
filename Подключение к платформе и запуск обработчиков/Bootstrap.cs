using System;
using System.IO;
using System.Reflection;
namespace Agora.Outgoing.Workflow {
 public static class Bootstrap {
  static bool initialized;
  public static void Initialize(){lock(typeof(Bootstrap)){if(initialized)return;AppDomain.CurrentDomain.AssemblyResolve+=Resolve;initialized=true;}}
  static Assembly Resolve(object sender,ResolveEventArgs args){string name=new AssemblyName(args.Name).Name;foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name==name)return a;foreach(string dir in new[]{@"C:\AGORA-OUT\csharp-runtime",@"C:\Program Files (x86)\Docsvision\5.5\Client",@"C:\Program Files (x86)\Docsvision\5.5\Client\BaseObjects",@"C:\Program Files (x86)\Docsvision\5.5\Client\ApprovalDesigner",@"C:\Program Files (x86)\Docsvision\5.5\Client\Components",@"C:\AGORA-OUT\pdf-runtime\pdfsharp",@"C:\Program Files (x86)\Docsvision\Platform\5.5"}){string file=Path.Combine(dir,name+".dll");if(File.Exists(file))return Assembly.LoadFrom(file);}return null;}
  public static void Run(string mode){Initialize();Runtime.Run(mode);}
 }
}
