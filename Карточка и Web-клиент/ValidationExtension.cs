using System;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.StorageServer.Extensibility;
[assembly: System.Reflection.AssemblyVersion("1.9.5.0")]
namespace Agora.Outgoing.Server {
 public sealed partial class ValidationExtension : StorageServerExtension {
  static readonly Guid DocumentType=new Guid("b9f7bfd7-7429-455e-a3f1-94ffb569c794");
  static readonly Guid Kind=new Guid("7cf57200-ae68-4bca-ba7f-2243a9f783bc");
  static readonly Guid Main=new Guid("30eb9b87-822b-4753-9a50-a1825dca1b74");
  static readonly Guid SystemSection=new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06");
  static readonly Guid Extra=new Guid("a60f4e32-03be-4d47-810a-a595ed51796d");
  static readonly Guid Universal=new Guid("4538149d-1fc7-4d41-a104-890342c6b4f8");
  static readonly Guid UniversalItems=new Guid("1b1a44fb-1fb1-4876-83aa-95ad38907e24");
  void Trace(string stage){try{System.IO.File.AppendAllText(@"C:\ProgramData\Docsvision\AGORA-OUT\server-validation.log",DateTime.UtcNow.ToString("o")+" "+stage+Environment.NewLine);}catch{}}
  [ExtensionMethod] public string GetContextInfo(){return Request.Session.UserAccount+"|"+Request.Base.Name;}
  [ExtensionMethod] public string GetVersion(){return "AGORA-OUT.Validation/1.9.5";}
  [ExtensionMethod] public string ValidateDraft(Guid cardId){return Validate(cardId,false);}
  [ExtensionMethod] public string ValidateForApproval(Guid cardId){return Validate(cardId,true);}
  static Guid Id(RowData row,string name){return row==null?Guid.Empty:row.GetGuid(name).GetValueOrDefault();}
  static bool Empty(RowData row,string name){return row==null || String.IsNullOrWhiteSpace(row.GetString(name));}
  sealed class Result {
   public readonly XmlDocument Xml=new XmlDocument();public readonly XmlElement Root;public readonly XmlElement Errors;int count;
   public Result(Guid id,bool approval){Root=Xml.CreateElement("ValidationResult");Xml.AppendChild(Root);Root.SetAttribute("version","1");Root.SetAttribute("cardId",id.ToString());Root.SetAttribute("stage",approval?"Approval":"Draft");Root.SetAttribute("scope","saved-data-preflight");Errors=Xml.CreateElement("Errors");Root.AppendChild(Errors);}
   public void Error(string code,string field,string message){var e=Xml.CreateElement("Error");e.SetAttribute("code",code);e.SetAttribute("field",field);e.InnerText=message;Errors.AppendChild(e);count++;}
   public override string ToString(){Root.SetAttribute("valid",count==0?"true":"false");Root.SetAttribute("errorCount",count.ToString(System.Globalization.CultureInfo.InvariantCulture));return Xml.OuterXml;}
  }
  static void Reference(UserSession session,RowData row,string field,Guid expectedType,string label,Result result){
   Guid id=Id(row,field);if(id==Guid.Empty)return;
   var section=session.CardManager.GetCardData(Universal).Sections[UniversalItems];
   if(!section.RowExists(id) || section.GetRow(id).SubSection.ParentRow==null || section.GetRow(id).SubSection.ParentRow.Id!=expectedType)result.Error("InvalidDictionaryValue",field,"Выберите значение поля «"+label+"» из соответствующего справочника АГОРЫ.");
  }
  string Validate(Guid cardId,bool approval){
   // Reuse the caller's session, including its access rights. Never open an administrator session here.
   var result=new Result(cardId,approval);
   try {
   Trace("before CreateSession");
   var session=CreateSession(true);
   Trace("after CreateSession");
   var card=session.CardManager.GetCardData(cardId);
   Trace("after GetCardData");
   if(card.Type.Id!=DocumentType){result.Error("WrongCardType","Kind","Проверка поддерживает только исходящие письма АГОРЫ.");return result.ToString();}
   var sys=card.Sections[SystemSection].FirstRow;
   var main=card.Sections[Main].FirstRow;
   Guid kind=Id(sys,"Kind");if(kind==Guid.Empty)kind=Id(main,"Kind");
   if(kind!=Kind){result.Error("WrongKind","Kind","Проверка поддерживает только исходящие письма АГОРЫ.");return result.ToString();}
   var extra=card.Sections[Extra].FirstRow;
   if(main!=null){int? sheets=main.GetInt32("NumberOfSheets");if(sheets.HasValue && sheets.Value<0)result.Error("NegativeSheetCount","NumberOfSheets","Количество листов должно быть целым неотрицательным числом.");}
   Reference(session,extra,"DocumentType",new Guid("10269db6-f8e1-4e2c-8e11-a873536729c9"),"Тип документа",result);
   Reference(session,extra,"Journal",new Guid("6a911880-d1c5-4511-abde-c0b45e4c1f6e"),"Журнал",result);
   Reference(session,extra,"DeliveryMethod",new Guid("39df3f1e-aeb5-4aad-b80d-437b9125aaf0"),"Способ отправки",result);
   Reference(session,extra,"SigningMethod",new Guid("c010fdcf-e7a7-44cf-9e20-9c7ef1067771"),"Способ подписания",result);
   if(approval){
    if(Empty(main,"Name"))result.Error("Required","Name","Заполните тему письма.");
    if(Empty(main,"Content"))result.Error("Required","Content","Заполните содержание письма.");
    if(Id(main,"Author")==Guid.Empty)result.Error("Required","Author","Не определён инициатор письма.");
    if(Id(main,"RegistrationPlaceId")==Guid.Empty)result.Error("Required","RegistrationPlaceId","Не определено место регистрации.");
    if(Id(extra,"DocumentType")==Guid.Empty)result.Error("Required","DocumentType","Выберите тип документа.");
    if(Id(extra,"Journal")==Guid.Empty)result.Error("Required","Journal","Не определён журнал регистрации.");
    if(Id(extra,"InitiatorManager")==Guid.Empty)result.Error("ManagerNotConfigured","InitiatorManager","Не настроен руководитель инициатора для маршрута согласования.");
    int recipients=0;foreach(RowData row in card.Sections[new Guid("9e5f7ced-2d4a-44c0-8a23-14652e7b373f")].Rows)if(Id(row,"ReceiverPartnerCo")!=Guid.Empty)recipients++;
    if(recipients==0)result.Error("Required","ReceiversPartners","Выберите организацию-получателя.");
    int signers=0;foreach(RowData row in card.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows)if(Id(row,"Signer")!=Guid.Empty)signers++;
    if(signers!=1)result.Error("OneSignerRequired","Signers","Выберите одного подписанта письма.");
    bool file=false;foreach(RowData row in card.Sections[new Guid("a6fa8baf-2ea4-4071-aa3e-5c4e71646a90")].Rows)if(row.GetInt32("FileType").GetValueOrDefault(-1)==0 && Id(row,"FileId")!=Guid.Empty)file=true;
    if(!file)result.Error("MainFileRequired","Files","Сформируйте или добавьте основной файл письма.");
   }
   Trace("validation finished");return result.ToString();
   }catch(Exception ex){Trace(ex.ToString());result.Error("ServerValidationUnavailable","","Серверная проверка временно недоступна. Обратитесь к администратору.");return result.ToString();}
  }
 }
}