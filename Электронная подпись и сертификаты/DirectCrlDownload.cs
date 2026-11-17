using System;using System.IO;using System.Net;using System.Net.Sockets;using System.Text;
public static class AgoraDirectCrlDownload {
 public static void Download(string url,int interfaceIndex,string path){
  Uri uri=new Uri(url);if(uri.Scheme!="http" || (uri.Host!="uc.nalog.ru" && uri.Host!="crl.gosuslugi.ru"))throw new InvalidOperationException("Unexpected CRL source");
  using(var tcp=new TcpClient(AddressFamily.InterNetwork)){
   tcp.Client.SetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31,IPAddress.HostToNetworkOrder(interfaceIndex));
   IPAddress address=null;foreach(var ip in Dns.GetHostAddresses(uri.Host)){if(ip.AddressFamily==AddressFamily.InterNetwork){address=ip;break;}}if(address==null)throw new IOException("No IPv4 CRL endpoint");
   var connection=tcp.BeginConnect(address,uri.Port,null,null);if(!connection.AsyncWaitHandle.WaitOne(15000))throw new IOException("CRL connection timeout");tcp.EndConnect(connection);
   tcp.ReceiveTimeout=30000;tcp.SendTimeout=15000;using(var stream=tcp.GetStream()){
    var request=Encoding.ASCII.GetBytes("GET "+uri.PathAndQuery+" HTTP/1.0\r\nHost: "+uri.Host+"\r\nAccept: application/pkix-crl\r\nConnection: close\r\n\r\n");stream.Write(request,0,request.Length);
    using(var header=new MemoryStream()){
     int previous=0,match=0;while(match<4){int next=stream.ReadByte();if(next<0)throw new IOException("Incomplete CRL HTTP headers");header.WriteByte((byte)next);if(header.Length>32768)throw new IOException("CRL headers too large");if((match==0||match==2)&&next==13)match++;else if((match==1||match==3)&&next==10)match++;else match=next==13?1:0;previous=next;}
     string headers=Encoding.ASCII.GetString(header.ToArray());string[] lines=headers.Split(new[]{"\r\n"},StringSplitOptions.None);if(!lines[0].Contains(" 200 "))throw new IOException("CRL HTTP status: "+lines[0]);long expected=-1;foreach(string line in lines){if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))expected=long.Parse(line.Substring(15).Trim());if(line.StartsWith("Transfer-Encoding:",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected encoded CRL response");}
     if(expected>33554432)throw new IOException("CRL too large");long total=0;using(var file=File.Create(path)){byte[] buffer=new byte[65536];int read;while((read=stream.Read(buffer,0,buffer.Length))>0){total+=read;if(total>33554432)throw new IOException("CRL size limit exceeded");file.Write(buffer,0,read);}}if(total==0 || (expected>=0 && total!=expected))throw new IOException("Incomplete CRL payload");
    }
   }
  }
 }
}
