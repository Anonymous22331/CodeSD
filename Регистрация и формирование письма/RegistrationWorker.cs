using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Security.Cryptography;
using DocsVision.Platform.ObjectManager;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace Agora.Outgoing.Workflow {
 public static partial class Runtime {
  public sealed class RegistrationManifest {public string card,number,sourceVersion,signatureTime,certificate,sha256,createdUtc;}
  static string Hash(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
  static void RegisterPending(){string jobs=Path.Combine(StateRoot,"registration-jobs");Directory.CreateDirectory(jobs);var service=C.GetService<IDocumentService>();int failed=0;
   foreach(CardData card in S.CardManager.FindCards(Query(new Guid("d1754352-3891-46fa-9fd5-61cee8844443")).GetXml())){bool locked=false;string download=null,work=null;try{
    if(card.LockStatus!=LockStatus.Free)continue;card.PlaceLock();locked=true;card.Refresh();var prepared=Call("PrepareOutgoingRegistration",Arg("cardId",card.Id));var root=prepared.DocumentElement;if(root.GetAttribute("status")=="unchanged")continue;
    var proof=(XmlElement)root.SelectSingleNode("SignatureConfirmationResult");if(proof==null||proof.GetAttribute("status")!="verified")throw new InvalidOperationException("Verified signature proof is required");Guid source=new Guid(root.GetAttribute("sourceVersion"));string number=root.GetAttribute("number");DateTime date=DateTime.ParseExact(root.GetAttribute("date"),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    var document=C.GetObject<Document>(card.Id);C.RefreshObject(document);var mainFiles=document.Files.Where(f=>f.FileType==DocumentFileType.Main).ToArray();if(mainFiles.Length!=1||mainFiles[0].FileVersionId!=source)throw new InvalidOperationException("Source main file version changed");
    string safe=number;foreach(char ch in Path.GetInvalidFileNameChars())safe=safe.Replace(ch,'_');string filename="Подписанный вариант - "+safe+".pdf",manifestPath=Path.Combine(jobs,card.Id.ToString("N")+".json");var manifest=File.Exists(manifestPath)?Read<RegistrationManifest>(manifestPath):null;var existing=document.Files.Where(f=>f.FileType==DocumentFileType.Additional&&f.FileName==filename).ToArray();if(existing.Length>1)throw new InvalidOperationException("Multiple PDFs require recovery");DocumentFile pdf;
    if(existing.Length==1){if(manifest==null||manifest.sourceVersion!=source.ToString()||manifest.number!=number||manifest.signatureTime!=proof.GetAttribute("signatureTime")||manifest.certificate!=proof.GetAttribute("certificate"))throw new InvalidOperationException("Attached PDF has no matching generation manifest");download=service.DownloadAdditionalFile(document,existing[0]);if(Hash(download)!=manifest.sha256)throw new InvalidOperationException("Attached PDF differs from generation manifest");pdf=existing[0];}
    else{download=service.DownloadMainFile(document,mainFiles[0]);work=Path.Combine(Path.GetTempPath(),"AgoraRegistration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);string output=Path.Combine(work,filename);RegisteredPdf.Create(download,output,number,date,proof);manifest=new RegistrationManifest{card=card.Id.ToString(),number=number,sourceVersion=source.ToString(),signatureTime=proof.GetAttribute("signatureTime"),certificate=proof.GetAttribute("certificate"),sha256=Hash(output),createdUtc=DateTime.UtcNow.ToString("o")};Save(manifestPath,manifest);pdf=service.AddAdditionalFile(document,output);C.SaveObject(document);C.AcceptChanges();}
    Call("FinalizeOutgoingRegistration",Arg("cardId",card.Id),Arg("pdfRowId",pdf.GetObjectId()),Arg("sourceVersion",source));Log("registration",new {status="registered",card=card.Id,number=number});
   }catch(Exception ex){failed++;C.RollbackChanges();Log("registration",new {card=card.Id,error=ex.ToString()});}finally{if(download!=null&&File.Exists(download))File.Delete(download);if(work!=null&&Directory.Exists(work))Directory.Delete(work,true);if(locked)card.RemoveLock();}}
   if(failed>0)throw new InvalidOperationException("Registration failed cards: "+failed);
  }
 }
}
