using System;using System.IO;using System.Reflection;using System.Collections.Generic;
public static class AgoraAssemblyResolver {
 static HashSet<string> busy=new HashSet<string>();
 public static void Init(){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;}
 static Assembly Resolve(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;if(!busy.Add(n))return null;try{foreach(var a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name==n)return a;foreach(string dir in new[]{@"C:\Program Files (x86)\Docsvision\5.5\Client\Components",@"C:\Program Files (x86)\Docsvision\5.5\Client\BaseObjects",@"C:\Program Files (x86)\Docsvision\5.5\Client"}){string p=Path.Combine(dir,n+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;}finally{busy.Remove(n);}}
}