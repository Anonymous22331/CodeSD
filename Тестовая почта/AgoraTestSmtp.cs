using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
public static class AgoraTestSmtp {
 public static void Run(string directory,int port) {
  Directory.CreateDirectory(directory);var listener=new TcpListener(IPAddress.Loopback,port);listener.Start(8);
  File.WriteAllText(Path.Combine(directory,"listener.json"),"{\"status\":\"listening\",\"bind\":\"127.0.0.1\",\"port\":"+port+",\"internetDelivery\":false}");
  try {while(true){using(var client=listener.AcceptTcpClient()){client.ReceiveTimeout=30000;client.SendTimeout=30000;try{Accept(client,directory);}catch(Exception e){File.AppendAllText(Path.Combine(directory,"errors.log"),DateTime.UtcNow.ToString("o")+" "+e.GetType().Name+Environment.NewLine);}}}}finally{listener.Stop();}
 }
 static void Accept(TcpClient client,string directory) {
  using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true))using(var writer=new StreamWriter(stream,new UTF8Encoding(false),4096,true)){writer.NewLine="\r\n";writer.AutoFlush=true;writer.WriteLine("220 agora-test ESMTP");bool sender=false;int recipients=0;string line;
   while((line=reader.ReadLine())!=null){if(line.Length>8192){writer.WriteLine("500 Command too long");return;}
    if(line.StartsWith("EHLO ",StringComparison.OrdinalIgnoreCase)){writer.WriteLine("250-agora-test");writer.WriteLine("250 SIZE 10485760");}
    else if(line.StartsWith("HELO ",StringComparison.OrdinalIgnoreCase)){writer.WriteLine("250 agora-test");}
    else if(line.StartsWith("MAIL FROM:",StringComparison.OrdinalIgnoreCase)){sender=true;recipients=0;writer.WriteLine("250 OK");}
    else if(line.StartsWith("RCPT TO:",StringComparison.OrdinalIgnoreCase)){if(!sender){writer.WriteLine("503 MAIL first");continue;}var address=line.Substring(8).Trim().Trim('<','>');if(!address.EndsWith("@agora.test",StringComparison.OrdinalIgnoreCase)){writer.WriteLine("550 Test domain only");continue;}if(recipients>=20){writer.WriteLine("452 Too many recipients");continue;}recipients++;writer.WriteLine("250 OK");}
    else if(line.Equals("DATA",StringComparison.OrdinalIgnoreCase)){if(!sender || recipients==0){writer.WriteLine("503 MAIL and RCPT required");continue;}writer.WriteLine("354 End with dot");var data=new StringBuilder();bool complete=false;while((line=reader.ReadLine())!=null){if(line=="."){complete=true;break;}if(line.StartsWith("..",StringComparison.Ordinal))line=line.Substring(1);data.Append(line).Append("\r\n");if(data.Length>10485760){writer.WriteLine("552 Message too large");return;}}if(!complete)return;var message=data.ToString();var match=Regex.Match(message,@"^X-AGORA-Notification-Key:\s*([a-zA-Z0-9-]+)\s*$",RegexOptions.Multiline);var key=match.Success?match.Groups[1].Value:Guid.NewGuid().ToString("N");var path=Path.Combine(directory,key+".eml");if(!File.Exists(path)){var temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");File.WriteAllText(temp,"Received: from localhost by agora-test; "+DateTime.UtcNow.ToString("r")+"\r\n"+message,new UTF8Encoding(false));try{File.Move(temp,path);}catch{File.Delete(temp);if(!File.Exists(path))throw;}}sender=false;recipients=0;writer.WriteLine("250 Message stored");}
    else if(line.Equals("RSET",StringComparison.OrdinalIgnoreCase)){sender=false;recipients=0;writer.WriteLine("250 OK");}
    else if(line.Equals("NOOP",StringComparison.OrdinalIgnoreCase)){writer.WriteLine("250 OK");}
    else if(line.Equals("QUIT",StringComparison.OrdinalIgnoreCase)){writer.WriteLine("221 Bye");return;}
    else{writer.WriteLine("502 Command not implemented");}
   }
  }
 }
}
