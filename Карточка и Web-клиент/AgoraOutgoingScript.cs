using System;
using System.Collections.Generic;
using System.Linq;
using DocsVision.Platform.Extensibility;
using DocsVision.Platform.CardHost;
using DocsVision.Platform.WinForms;
using DocsVision.BackOffice.WinForms;
using DocsVision.BackOffice.WinForms.Design.LayoutItems;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
namespace BackOffice {
 [CardKindScriptClass]
 public class AgoraOutgoingScript : ScriptClassBase {
  bool initialized;
  System.Windows.Forms.Control cancellationButton;
  System.Windows.Forms.Control generationButton;
  bool generatingLetter;
  System.Windows.Forms.Control sendingButton;
  bool markingSent;
  System.Windows.Forms.Control approvalButton;
  bool startingApproval;
  System.Windows.Forms.Control taskDecisionButton;
  bool completingTask;
  System.Windows.Forms.Control routeSelector;
  void Trace(string message) {try {var dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AgoraOutgoing");System.IO.Directory.CreateDirectory(dir);System.IO.File.AppendAllText(System.IO.Path.Combine(dir,"client-rules.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch {}}
  static readonly Guid GoUnit = new Guid("4900f7b1-472c-447d-bc0a-7d460937ceb8");
  static readonly Guid KazanUnit = new Guid("691c36db-f7da-4497-9f9b-6930083bf85c");
  static readonly Guid SaratovUnit = new Guid("617ac655-38d4-4dab-9654-7865dc630b10");
  public static readonly Guid GoJournal = new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e");
  public static readonly Guid KazanJournal = new Guid("08137765-5066-4e5d-a189-4f459e32feab");
  public static readonly Guid SaratovJournal = new Guid("adc70c7d-54d7-4a13-aa6f-2cc208f572b7");
  public static Guid JournalForUnit(StaffUnit unit) {
   var seen = new HashSet<Guid>();
   while(unit != null && seen.Add(unit.GetObjectId())) {
    Guid id = unit.GetObjectId();
    if(id == GoUnit) return GoJournal;
    if(id == KazanUnit) return KazanJournal;
    if(id == SaratovUnit) return SaratovJournal;
    unit = unit.ParentUnit;
   }
   return Guid.Empty;
  }
  public static StaffEmployee ManagerForUnit(StaffUnit unit,Guid journal) {
   if(journal==KazanJournal || journal==SaratovJournal){var root=unit;var roots=new HashSet<Guid>();while(root!=null && root.ParentUnit!=null && roots.Add(root.GetObjectId()))root=root.ParentUnit;var pending=new Stack<StaffUnit>();if(root!=null)pending.Push(root);var visited=new HashSet<Guid>();while(pending.Count>0){var current=pending.Pop();if(!visited.Add(current.GetObjectId()))continue;if(current.GetObjectId()==new Guid("98f2635a-02a4-4af7-92ca-f4bb7674434d"))return current.Manager;foreach(var child in current.Units)pending.Push(child);}return null;}
   var seen=new HashSet<Guid>();
   while(unit!=null && seen.Add(unit.GetObjectId())) {
    if(journal==GoJournal && unit.Manager!=null)return unit.Manager;

    unit=unit.ParentUnit;
   }
   return null;
  }
  ILayoutPropertyItem Item(string name) {return ((ICustomizableControl)CardControl).FindPropertyItem<ILayoutPropertyItem>(name);}
  object Value(string name) {var item=Item(name);return item == null ? null : item.ControlValue;}
  Guid Id(string name) {object v=Value(name);if(v is Guid)return (Guid)v;Guid id;return v!=null && Guid.TryParse(v.ToString(),out id)?id:Guid.Empty;}
  void Put(string name,object value) {var item=Item(name);if(item==null)throw new InvalidOperationException("АГОРА: отсутствует свойство разметки "+name);item.ControlValue=value;item.Commit();}
  public void CardDocument_CardActivated(object sender, CardActivatedEventArgs e) {
   Trace("CardActivated "+e.ActivateFlags+" "+e.ActivateMode);
   if(e.ActivateMode!=ActivateMode.Cache)CardControl.BeginInvoke(new Action(ConnectGenerationButton));
   if(initialized || (e.ActivateFlags & ActivateFlags.New)==0 || e.ActivateMode==ActivateMode.Cache)return;
   initialized=true;
   CardControl.BeginInvoke(new Action(delegate {try {InitializeNewDocument();Trace("New defaults applied");}catch(Exception ex){Trace("New defaults error: "+ex.Message);System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: заполнение реквизитов");}}));
  }
  void ConnectCancellationButton(){try{var item=Item("AgoraCancelDocument");var p=item==null?null:item.GetType().GetProperty("Control");var button=p==null?null:p.GetValue(item,null) as System.Windows.Forms.Control;if(button==null)return;var state=CardControl.CardData.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow;button.Visible=state!=null && state.GetGuid("State")==new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6");if(cancellationButton==button)return;cancellationButton=button;button.Click+=delegate{try{if(System.Windows.Forms.MessageBox.Show("Отменить подготовку письма? Несохранённые изменения не будут записаны.","АГОРА",System.Windows.Forms.MessageBoxButtons.YesNo)!=System.Windows.Forms.DialogResult.Yes)return;var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","CancelOutgoingDocument");method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);CardControl.CardData.Refresh();var doc=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);CardControl.ObjectContext.RefreshObject(doc);RefreshControls();CardControl.CardFrame.Refresh();}catch(Exception ex){System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: отмена");}};}catch(Exception ex){Trace("Cancellation button: "+ex.Message);}}
  bool SaveBeforeBusinessAction(){return CardControl.ReadOnly || CardControl.ActivateMode==ActivateMode.ReadOnly || CardControl.ActivateMode==ActivateMode.Preview || CardControl.Save();}
  void ConnectGenerationButton() {
   ConnectCancellationButton();
   ConnectSendingButton();
   ConnectApprovalButton();
   ConnectTaskDecisionButton();
   ConfigureNormalControlEditors();
   try {
    var item=Item("AgoraGenerateLetter");if(item==null)return;
    var property=item.GetType().GetProperty("Control");var button=property==null?null:property.GetValue(item,null) as System.Windows.Forms.Control;
    if(button==null || button==generationButton)return;
    if(generationButton!=null)generationButton.Click-=GenerateLetter;
    generationButton=button;generationButton.Click+=GenerateLetter;
    Trace("Generation button connected");
   }catch(Exception ex){Trace("Generation button initialization failed: "+ex.ToString());}finally{ConfigureJournalEditor();}
  }
  void ConfigureJournalEditor(){try{var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","GetOutgoingWebContext");method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);method.Parameters.AddNew("initialize",DocsVision.Platform.ObjectManager.ParameterValueType.Bool,false);var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());var item=Item("AgoraJournal");var p=item==null?null:item.GetType().GetProperty("Control");var control=p==null?null:p.GetValue(item,null) as System.Windows.Forms.Control;if(control!=null)control.Enabled=xml.DocumentElement.GetAttribute("journalEdit")=="true";if(generationButton!=null)generationButton.Visible=xml.DocumentElement.GetAttribute("canGenerate")=="true";if(approvalButton!=null)approvalButton.Visible=xml.DocumentElement.GetAttribute("canStart")=="true";if(sendingButton!=null)sendingButton.Visible=xml.DocumentElement.GetAttribute("shippingEdit")=="true";if(cancellationButton!=null)cancellationButton.Visible=xml.DocumentElement.GetAttribute("canCancel")=="true";}catch(Exception ex){Trace("Journal editor: "+ex.Message);}}
  void ConfigureNormalControlEditors() {
   try {
    var raw=CardControl.Session.CardManager.GetCardData(CardControl.CardData.Id);
    var sys=raw.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow;
    if(sys==null || sys.GetGuid("State")!=new Guid("b7be72ff-4ede-490d-85db-22ee324e27f4"))return;
    bool allowed=false;
    try {var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","GetOutgoingTaskActions");method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,raw.Id);var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());allowed=xml.SelectSingleNode("/TaskActions[@status='ok']/Task[@normalControl='true']")!=null;}catch(Exception ex){Trace("Normal control access: "+ex.Message);}
    foreach(string name in new[]{"Signer","Content"}){var item=Item(name);var property=item==null?null:item.GetType().GetProperty("Control");var control=property==null?null:property.GetValue(item,null) as System.Windows.Forms.Control;if(control!=null)control.Enabled=allowed;}
   }catch(Exception ex){Trace("Normal control editors: "+ex.Message);}
  }
  void ConnectTaskDecisionButton() {
   try {
    var item=Item("AgoraTaskDecision");if(item==null)return;var property=item.GetType().GetProperty("Control");var button=property==null?null:property.GetValue(item,null) as System.Windows.Forms.Control;
    if(button==null || button==taskDecisionButton)return;if(taskDecisionButton!=null)taskDecisionButton.Click-=ShowTaskDecisions;taskDecisionButton=button;button.Click+=ShowTaskDecisions;
   }catch(Exception ex){Trace("Task decision button: "+ex.Message);}
  }
  string DecisionComment(bool required) {
   using(var form=new System.Windows.Forms.Form()) {
    form.Text="Комментарий к решению";form.Width=540;form.Height=260;form.StartPosition=System.Windows.Forms.FormStartPosition.CenterParent;
    var text=new System.Windows.Forms.TextBox{Multiline=true,Dock=System.Windows.Forms.DockStyle.Fill,MaxLength=8000,ScrollBars=System.Windows.Forms.ScrollBars.Vertical};var panel=new System.Windows.Forms.FlowLayoutPanel{Dock=System.Windows.Forms.DockStyle.Bottom,Height=42,FlowDirection=System.Windows.Forms.FlowDirection.RightToLeft};
    var ok=new System.Windows.Forms.Button{Text="Принять решение",Width=160,DialogResult=System.Windows.Forms.DialogResult.OK};var cancel=new System.Windows.Forms.Button{Text="Отмена",DialogResult=System.Windows.Forms.DialogResult.Cancel};panel.Controls.Add(ok);panel.Controls.Add(cancel);form.Controls.Add(text);form.Controls.Add(panel);form.AcceptButton=ok;form.CancelButton=cancel;
    if(form.ShowDialog(CardControl)!=System.Windows.Forms.DialogResult.OK)return null;if(required && String.IsNullOrWhiteSpace(text.Text))throw new InvalidOperationException("Укажите комментарий к решению.");return text.Text;
   }
  }
  void ShowTaskDecisions(object sender,EventArgs e) {
   try {
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","GetOutgoingTaskActions");method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);
    var nodes=xml.SelectNodes("/TaskActions/Task");
    var menu=new System.Windows.Forms.ContextMenuStrip();menu.Closed+=delegate{menu.Dispose();};
    foreach(System.Xml.XmlElement node in nodes){Guid taskId=new Guid(node.GetAttribute("id"));var parent=new System.Windows.Forms.ToolStripMenuItem(node.GetAttribute("name"));menu.Items.Add(parent);
     if(node.GetAttribute("signing")=="true"){var open=new System.Windows.Forms.ToolStripMenuItem("Подписать ЭП — открыть задание");open.Click+=delegate{CardControl.CardFrame.CloseAndOpen(taskId,null);};parent.DropDownItems.Add(open);}
     foreach(System.Xml.XmlElement decision in node.SelectNodes("Decision")){int semantic=Int32.Parse(decision.GetAttribute("semantic"));bool required=decision.GetAttribute("comment")=="true";var option=new System.Windows.Forms.ToolStripMenuItem(decision.GetAttribute("name"));option.Click+=delegate{CompleteTaskDecision(taskId,semantic,required);};parent.DropDownItems.Add(option);}
    }
    var operations=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","GetOutgoingDocumentOperations");operations.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);var available=new System.Xml.XmlDocument();available.LoadXml((string)operations.Execute());if(available.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(available.DocumentElement.InnerText);foreach(System.Xml.XmlElement operation in available.SelectNodes("/DocumentOperations/Operation")){int row=Int32.Parse(operation.GetAttribute("row"));var command=new System.Windows.Forms.ToolStripMenuItem(operation.GetAttribute("name"));command.Click+=delegate{RunDocumentOperation(row);};menu.Items.Add(command);}if(menu.Items.Count==0){menu.Dispose();System.Windows.Forms.MessageBox.Show("Нет доступных действий в текущем состоянии.","АГОРА");return;}menu.Show(taskDecisionButton,new System.Drawing.Point(0,taskDecisionButton.Height));
   }catch(Exception ex){Trace("Show task decisions: "+ex.ToString());System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: согласование");}
  }
  string DocumentText(string caption,bool multiline) {
   using(var form=new System.Windows.Forms.Form()){form.Text=caption;form.Width=560;form.Height=multiline?330:155;form.StartPosition=System.Windows.Forms.FormStartPosition.CenterParent;var input=new System.Windows.Forms.TextBox{Left=12,Top=12,Width=515,Height=multiline?220:25,Multiline=multiline};var ok=new System.Windows.Forms.Button{Text="Продолжить",Left=310,Top=multiline?240:55,Width=105,DialogResult=System.Windows.Forms.DialogResult.OK};var cancel=new System.Windows.Forms.Button{Text="Отмена",Left=425,Top=ok.Top,Width=100,DialogResult=System.Windows.Forms.DialogResult.Cancel};form.Controls.Add(input);form.Controls.Add(ok);form.Controls.Add(cancel);form.AcceptButton=ok;form.CancelButton=cancel;return form.ShowDialog(CardControl)==System.Windows.Forms.DialogResult.OK?input.Text:null;}
  }
  void RunDocumentOperation(int row) {
   try {
    string methodName=row==27?"CreateOutgoingFamiliarizationDraft":row==35?"GetOutgoingApprovalHistory":"SendOutgoingDocumentMessage";var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation",methodName);method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);
    if(row==28){string recipient=DocumentText("Адрес получателя — домен agora.test",false);if(recipient==null)return;string message=DocumentText("Сообщение по документу",true);if(message==null)return;method.Parameters.AddNew("recipientEmail",DocsVision.Platform.ObjectManager.ParameterValueType.String,recipient);method.Parameters.AddNew("message",DocsVision.Platform.ObjectManager.ParameterValueType.String,message);}
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);
    if(row==27 || row==35){Guid id=new Guid(xml.DocumentElement.GetAttribute("reconcile"));CardControl.CardFrame.CloseAndOpen(id,null);}else{System.Windows.Forms.MessageBox.Show(xml.DocumentElement.InnerText,"АГОРА: почта");}
   }catch(Exception ex){Trace("Document operation: "+ex);System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА");}
  }
  void CompleteTaskDecision(Guid taskId,int semantic,bool required) {
   if(completingTask)return;completingTask=true;
   try {
    string comment=required?DecisionComment(true):"";if(comment==null)return;
    var state=CardControl.CardData.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow.GetGuid("State");if((state==new Guid("bcc23707-c0aa-4b2c-bbec-3b6ff751aca5") || state==new Guid("b7be72ff-4ede-490d-85db-22ee324e27f4")) && semantic!=4 && semantic!=2 && !SaveBeforeBusinessAction())return;
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","CompleteOutgoingTask");method.Parameters.AddNew("taskId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,taskId);method.Parameters.AddNew("semantic",DocsVision.Platform.ObjectManager.ParameterValueType.Integer,semantic);method.Parameters.AddNew("comment",DocsVision.Platform.ObjectManager.ParameterValueType.String,comment);
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);CardControl.CardData.Refresh();var doc=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);CardControl.ObjectContext.RefreshObject(doc);RefreshControls();CardControl.CardFrame.Refresh();
   }catch(Exception ex){Trace("Complete task decision: "+ex.ToString());System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: решение по заданию");}
   finally{completingTask=false;}
  }
  void ConnectApprovalButton() {
   try {
    var item=Item("AgoraStartApproval");if(item==null)return;
    var property=item.GetType().GetProperty("Control");var button=property==null?null:property.GetValue(item,null) as System.Windows.Forms.Control;
    if(button==null || button==approvalButton)return;
    if(approvalButton!=null)approvalButton.Click-=StartApproval;
    approvalButton=button;approvalButton.Click+=StartApproval;
    var selector=Item("AgoraShortRoute");var selectorProperty=selector==null?null:selector.GetType().GetProperty("Control");var control=selectorProperty==null?null:selectorProperty.GetValue(selector,null) as System.Windows.Forms.Control;
    if(control!=null && control!=routeSelector){if(routeSelector!=null){var oldEvent=routeSelector.GetType().GetEvent("EditValueChanged");if(oldEvent!=null)oldEvent.RemoveEventHandler(routeSelector,new EventHandler(RouteVariantChanged));}routeSelector=control;var changed=control.GetType().GetEvent("EditValueChanged");if(changed!=null)changed.AddEventHandler(control,new EventHandler(RouteVariantChanged));}RouteVariantChanged(null,EventArgs.Empty);
    var row=CardControl.CardData.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow;
    approvalButton.Visible=row!=null && row.GetGuid("State")==new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6");
   }catch(Exception ex){Trace("Approval button initialization: "+ex.ToString());}
  }
  void RouteVariantChanged(object sender,EventArgs e) {
   try {object value=Value("AgoraShortRoute");if(routeSelector!=null){var p=routeSelector.GetType().GetProperty("EditValue");if(p!=null)value=p.GetValue(routeSelector,null);}bool shortRoute=value is bool && (bool)value;if(approvalButton!=null)approvalButton.Text=shortRoute?"Зарегистрировать":"На согласование";}catch(Exception ex){Trace("Route selector: "+ex.Message);}
  }
  void StartApproval(object sender,EventArgs e) {
   if(startingApproval)return;startingApproval=true;
   try {
    if(!SaveBeforeBusinessAction())return;
    if(approvalButton!=null)approvalButton.Enabled=false;CardControl.UseWaitCursor=true;
    var extra=CardControl.CardData.Sections[new Guid("a60f4e32-03be-4d47-810a-a595ed51796d")].FirstRow;
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation",extra!=null && extra.GetBoolean("ShortRoute").GetValueOrDefault()?"BeginShortRegistration":"StartOutgoingApproval");
    method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());
    if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);
    CardControl.CardData.Refresh();var document=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);CardControl.ObjectContext.RefreshObject(document);RefreshControls();CardControl.CardFrame.Refresh();
   }catch(Exception ex){Trace("Start approval: "+ex.ToString());System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: согласование");}
   finally{CardControl.UseWaitCursor=false;if(approvalButton!=null)approvalButton.Enabled=true;startingApproval=false;}
  }
  void ConnectSendingButton() {
   try {
    var item=Item("AgoraMarkSent");if(item==null)return;
    var property=item.GetType().GetProperty("Control");var button=property==null?null:property.GetValue(item,null) as System.Windows.Forms.Control;
    if(button==null || button==sendingButton)return;
    if(sendingButton!=null)sendingButton.Click-=MarkSent;
    sendingButton=button;sendingButton.Click+=MarkSent;
    var row=CardControl.CardData.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow;
    sendingButton.Visible=row!=null && row.GetGuid("State")==new Guid("11b0eb5d-3fa1-48a9-ba0b-e9ed1c2b1510");
   }catch(Exception ex){Trace("Sending button initialization: "+ex.ToString());}
  }
  void MarkSent(object sender,EventArgs e) {
   if(markingSent)return;markingSent=true;
   try {
    if(!SaveBeforeBusinessAction())return;
    if(sendingButton!=null)sendingButton.Enabled=false;CardControl.UseWaitCursor=true;
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","MarkOutgoingSent");
    method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());
    if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);
    CardControl.CardData.Refresh();var document=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);CardControl.ObjectContext.RefreshObject(document);RefreshControls();CardControl.CardFrame.Refresh();
   }catch(Exception ex){Trace("Mark sent: "+ex.ToString());System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: отправка");}
   finally{CardControl.UseWaitCursor=false;if(sendingButton!=null)sendingButton.Enabled=true;markingSent=false;}
  }
  void GenerateLetter(object sender,EventArgs e) {
   if(generatingLetter)return;
   generatingLetter=true;
   try {
    if(!SaveBeforeBusinessAction())return;
    if(generationButton!=null)generationButton.Enabled=false;
    CardControl.UseWaitCursor=true;
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","GenerateMainLetter");
    method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());Trace("Generate letter: "+xml.OuterXml);
    if(xml.DocumentElement.GetAttribute("status")=="error")throw new InvalidOperationException(xml.DocumentElement.InnerText);
    CardControl.CardData.Refresh();var document=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);CardControl.ObjectContext.RefreshObject(document);RefreshControls();
    CardControl.CardFrame.Refresh();
    System.Windows.Forms.MessageBox.Show(xml.DocumentElement.InnerText,"АГОРА: исходящее письмо");
   }catch(Exception ex){Trace("Generate letter failed: "+ex.ToString());System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: формирование письма");}
   finally{CardControl.UseWaitCursor=false;if(generationButton!=null)generationButton.Enabled=true;generatingLetter=false;}
  }
  void InitializeNewDocument() {
   var employee=CardControl.ObjectContext.GetService<IStaffService>().GetCurrentEmployee();
   if(employee==null)return;
   Put("Author",employee.GetObjectId());
   if(Id("AgoraDocumentType")==Guid.Empty)Put("AgoraDocumentType",new Guid("620181fa-7a6a-4be9-883e-55f4060160d5"));
   if(employee.Unit!=null) {
    Put("AgoraRegistrationPlace",employee.Unit.GetObjectId());
    Guid journal=JournalForUnit(employee.Unit);
    Put("AgoraJournal",journal);
    var manager=ManagerForUnit(employee.Unit,journal);
    Put("AgoraInitiatorManager",manager==null?Guid.Empty:manager.GetObjectId());
   }
  }
  void ValidateDictionary(string property,Guid typeId,List<string> errors) {
   Guid id=Id(property);if(id==Guid.Empty)return;
   var dictionary=CardControl.ObjectContext.GetObject<BaseUniversal>(new Guid("4538149d-1fc7-4d41-a104-890342c6b4f8"));
   var type=dictionary.ItemTypes.FirstOrDefault(t=>t.GetObjectId()==typeId);
   if(type==null || !type.Items.Any(item=>item.GetObjectId()==id)) {string label=property=="AgoraDocumentType"?"Тип документа":property=="AgoraJournal"?"Журнал":property=="AgoraDeliveryMethod"?"Способ отправки":"Способ подписания";errors.Add("Выберите значение поля «"+label+"» из соответствующего справочника АГОРЫ.");}
  }
  public void CardDocument_Saving(object sender, System.ComponentModel.CancelEventArgs e) {try {ValidateDraft();}catch(InvalidOperationException ex){e.Cancel=true;Trace("Saving cancelled: "+ex.Message);System.Windows.Forms.MessageBox.Show(ex.Message,"АГОРА: проверка реквизитов");}}
  // Keep the original numbering fixture on its isolated test counters.
  static readonly Guid NumberingPilotCard = new Guid("56663665-052a-444d-bc94-157e01884749");
  bool assigningNumber;
  public void CardDocument_Saved(object sender, EventArgs e) {
   if(assigningNumber)return;
   try{var refresh=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","RequestOutgoingAccessRefresh");refresh.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);refresh.Execute();}catch(Exception ex){Trace("Access refresh queued by schedule: "+ex.Message);}
   var sys=CardControl.CardData.Sections[new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06")].FirstRow;
   if(sys==null || sys.GetGuid("State")!=new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6") || Id("AgoraJournal")==Guid.Empty)return;
   var employee=CardControl.ObjectContext.GetService<IStaffService>().GetCurrentEmployee();
   if(employee==null)return;
   assigningNumber=true;
   try {
    var method=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation",CardControl.CardData.Id==NumberingPilotCard?"AssignProvisionalNumber":"AssignDraftNumber");
    method.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);
    var xml=new System.Xml.XmlDocument();xml.LoadXml((string)method.Execute());
    var result=xml.DocumentElement;string status=result.GetAttribute("status");
    Trace("Saved numbering: "+xml.OuterXml);
    if(status=="error")throw new InvalidOperationException(result.InnerText);
    if(status!="unchanged") {
     CardControl.CardData.Refresh();
     var document=CardControl.ObjectContext.GetObject<Document>(CardControl.CardData.Id);
     CardControl.ObjectContext.RefreshObject(document);
     RefreshControls();
    }
   }catch(Exception ex) {
    Trace("Saved numbering pilot failed: "+ex.ToString());
    System.Windows.Forms.MessageBox.Show("Карточка сохранена, но присвоение временного номера не завершено. Повторите сохранение. "+ex.Message,"АГОРА: нумерация");
   }finally {assigningNumber=false;}
  }
  void ValidateDraft() {
   Trace("CardSaving validation started");
   var errors=new List<string>();
   object sheets=Value("AgoraNumberOfSheets");
   if(sheets!=null && sheets!=DBNull.Value && sheets.ToString()!="") {int count;if(!int.TryParse(sheets.ToString(),out count)||count<0)errors.Add("Количество листов должно быть целым неотрицательным числом.");}
   ValidateDictionary("AgoraDocumentType",new Guid("10269db6-f8e1-4e2c-8e11-a873536729c9"),errors);
   ValidateDictionary("AgoraJournal",new Guid("6a911880-d1c5-4511-abde-c0b45e4c1f6e"),errors);
   ValidateDictionary("AgoraDeliveryMethod",new Guid("39df3f1e-aeb5-4aad-b80d-437b9125aaf0"),errors);
   ValidateDictionary("AgoraSigningMethod",new Guid("c010fdcf-e7a7-44cf-9e20-9c7ef1067771"),errors);
   var journalCheck=CardControl.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation","ValidateOutgoingJournalChange");journalCheck.Parameters.AddNew("cardId",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,CardControl.CardData.Id);journalCheck.Parameters.AddNew("journal",DocsVision.Platform.ObjectManager.ParameterValueType.Guid,Id("AgoraJournal"));var journalResult=new System.Xml.XmlDocument();journalResult.LoadXml((string)journalCheck.Execute());if(journalResult.DocumentElement.GetAttribute("status")=="error")errors.Add(journalResult.DocumentElement.InnerText);
   foreach(string property in new[]{"Theme","Content","Author","AgoraInitiatorManager","AgoraRegistrationPlace","AgoraJournal","AgoraDocumentType","AgoraSigningMethod","Signer","Recipients"}){var item=Item(property);if(item!=null)item.Commit();}
   if(String.IsNullOrWhiteSpace(Convert.ToString(Value("Theme"))))errors.Add("Заполните тему письма.");
   if(String.IsNullOrWhiteSpace(Convert.ToString(Value("Content"))))errors.Add("Заполните содержание письма.");
   foreach(var field in new[]{new[]{"Author","Инициатор"},new[]{"AgoraInitiatorManager","Руководитель инициатора"},new[]{"AgoraRegistrationPlace","Место регистрации"},new[]{"AgoraJournal","Вид журнала"},new[]{"AgoraDocumentType","Тип документа"},new[]{"AgoraSigningMethod","Способ подписания"}})if(Id(field[0])==Guid.Empty)errors.Add("Заполните поле «"+field[1]+"».");
   var card=CardControl.CardData;
   var signers=card.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows.Cast<DocsVision.Platform.ObjectManager.RowData>().Count(r=>r.GetGuid("Signer").GetValueOrDefault()!=Guid.Empty);
   if(signers!=1)errors.Add("Выберите одного подписанта письма.");
   if(!card.Sections[new Guid("9e5f7ced-2d4a-44c0-8a23-14652e7b373f")].Rows.Cast<DocsVision.Platform.ObjectManager.RowData>().Any(r=>r.GetGuid("ReceiverPartnerCo").GetValueOrDefault()!=Guid.Empty))errors.Add("Выберите организацию-получателя.");
   if(!generatingLetter && !card.Sections[new Guid("a6fa8baf-2ea4-4071-aa3e-5c4e71646a90")].Rows.Cast<DocsVision.Platform.ObjectManager.RowData>().Any(r=>r.GetInt32("FileType").GetValueOrDefault(-1)==0 && r.GetGuid("FileId").GetValueOrDefault()!=Guid.Empty))errors.Add("Сформируйте или добавьте основной файл письма.");
   if(errors.Count!=0)throw new InvalidOperationException(string.Join(Environment.NewLine,errors.ToArray()));
  }
 }
}
