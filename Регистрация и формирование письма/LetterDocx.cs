using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
namespace Agora.Outgoing.Server {
 internal static class LetterDocx {
  const string W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
  public static void Generate(string template,string output,IDictionary<string,string> values,string body) {
   if(String.IsNullOrWhiteSpace(body))throw new InvalidOperationException("Заполните содержание письма.");
   File.Copy(template,output,true);
   using(var zip=ZipFile.Open(output,ZipArchiveMode.Update)) {
    var entry=zip.GetEntry("word/document.xml");var xml=new XmlDocument();xml.PreserveWhitespace=true;
    using(var reader=new StreamReader(entry.Open()))xml.LoadXml(reader.ReadToEnd());
    var ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("w",W);
    foreach(var pair in values) {
     var start=xml.SelectSingleNode("//w:bookmarkStart[@w:name='"+pair.Key+"']",ns) as XmlElement;
     if(start==null)throw new InvalidOperationException("Не найдена закладка "+pair.Key);
     var end=xml.SelectSingleNode("//w:bookmarkEnd[@w:id='"+start.GetAttribute("id",W)+"']",ns);
     if(end==null)throw new InvalidOperationException("Не закрыта закладка "+pair.Key);
     bool inside=false;var texts=new List<XmlElement>();
     foreach(XmlNode node in xml.SelectNodes("//*")){if(Object.ReferenceEquals(node,end))break;if(inside && node.LocalName=="t" && node.NamespaceURI==W)texts.Add((XmlElement)node);if(Object.ReferenceEquals(node,start))inside=true;}
     if(texts.Count==0)throw new InvalidOperationException("Нарушена структура закладки "+pair.Key);
     texts[0].InnerText=pair.Value??"";texts[0].SetAttribute("space","http://www.w3.org/XML/1998/namespace","preserve");
     for(int i=1;i<texts.Count;i++)texts[i].InnerText="";
    }
    var root=xml.SelectSingleNode("/w:document/w:body",ns);
    var header=xml.SelectSingleNode("/w:document/w:body/w:tbl[.//w:bookmarkStart[@w:name='РЕГ_НОМЕР']]",ns);
    var signature=xml.SelectSingleNode("/w:document/w:body/w:tbl[.//w:bookmarkStart[@w:name='ФИО_ПОДПИСАНТ']]",ns);
    if(header==null || signature==null)throw new InvalidOperationException("Не найдены блоки реквизитов и подписи.");
    var empty=new List<XmlNode>();var next=header.NextSibling;
    while(next!=null && !Object.ReferenceEquals(next,signature)) {
     if(next.LocalName!="p" || next.SelectNodes(".//w:drawing|.//w:pict|.//w:bookmarkStart|.//w:sectPr",ns).Count!=0 || !String.IsNullOrWhiteSpace(next.InnerText))throw new InvalidOperationException("Область содержания содержит данные; замена запрещена.");
     empty.Add(next);next=next.NextSibling;
    }
    if(next==null || empty.Count==0)throw new InvalidOperationException("Не найдена область содержания.");
    var style=empty[0].SelectSingleNode("w:pPr",ns);
    foreach(string line in body.Replace("\r\n","\n").Replace('\r','\n').Split('\n')) {
     var p=xml.CreateElement("w","p",W);if(style!=null)p.AppendChild(style.CloneNode(true));
     var run=xml.CreateElement("w","r",W);var text=xml.CreateElement("w","t",W);text.SetAttribute("space","http://www.w3.org/XML/1998/namespace","preserve");text.InnerText=line;
     run.AppendChild(text);p.AppendChild(run);root.InsertBefore(p,signature);
    }
    foreach(var p in empty)root.RemoveChild(p);
    entry.Delete();entry=zip.CreateEntry("word/document.xml");
    using(var writer=XmlWriter.Create(entry.Open(),new XmlWriterSettings{Encoding=new UTF8Encoding(false)}))xml.Save(writer);
   }
  }
  public static string ContentHash(string path) {
   var parts=new List<string>();
   using(var zip=ZipFile.OpenRead(path))foreach(var entry in zip.Entries.OrderBy(e=>e.FullName,StringComparer.Ordinal))using(var stream=entry.Open())using(var sha=SHA256.Create())parts.Add(entry.FullName+":"+Convert.ToBase64String(sha.ComputeHash(stream)));
   using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n",parts))));
  }
 }
}
