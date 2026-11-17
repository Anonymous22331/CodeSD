using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
public static class LegacyScriptArchive {
 public static int Main(string[] args){if(args.Length!=1)return 2;try{Console.WriteLine(Archive(args[0]));return 0;}catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
 public static string Archive(string directory){string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);string[] files=Directory.GetFiles(root,"*.ps1",SearchOption.AllDirectories);if(files.Length==0)return "No PS1 files: "+root;string archive=Path.Combine(root,"Legacy-PowerShell-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip");
  using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))foreach(string file in files){string full=Path.GetFullPath(file);if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Path outside selected root");zip.CreateEntryFromFile(full,full.Substring(root.Length),CompressionLevel.Optimal);}
  using(var zip=ZipFile.OpenRead(archive))foreach(string file in files){string name=Path.GetFullPath(file).Substring(root.Length);var entry=zip.GetEntry(name);if(entry==null)throw new IOException("Archive entry missing");using(var sha=SHA256.Create())using(var original=File.OpenRead(file))using(var stored=entry.Open())if(!sha.ComputeHash(original).SequenceEqual(sha.ComputeHash(stored)))throw new IOException("Archive verification failed: "+name);}
  foreach(string file in files)File.Delete(file);string result="Archived and verified "+files.Length+" PS1 files: "+archive+Environment.NewLine+"Remaining PS1 files: "+Directory.GetFiles(root,"*.ps1",SearchOption.AllDirectories).Length;File.WriteAllText(Path.Combine(root,"LegacyScriptArchive-result.txt"),result);return result;
 }
}
