using System;
using System.Linq;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel;
using DocsVision.ApprovalDesigner.ObjectModel.Services;

namespace Agora.Outgoing.Server
{
    public sealed partial class ValidationExtension
    {
        /// <summary>
        /// Возвращает данные документа и доступные действия для Web-клиента.
        /// </summary>
        [ExtensionMethod]
        public string GetOutgoingWebContext(Guid cardId, bool initialize)
        {
            var xml = new XmlDocument();
            var root = xml.CreateElement("WebContext");
            xml.AppendChild(root);
            root.SetAttribute("status", "error");
            bool ownLock = false;
            CardData card = null;
            try
            {
                var session = CreateSession(true);
                var c = ApprovalContext(session);
                card = session.CardManager.GetCardData(cardId);
                if (card.Type.Id != DocumentType)
                {
                    throw new InvalidOperationException("Неподдерживаемый тип карточки.");
                }

                var sys = card.Sections[SystemSection].FirstRow;
                if (Id(sys, "Kind") != Kind)
                {
                    throw new InvalidOperationException("Неподдерживаемый вид карточки.");
                }

                var main = card.Sections[Main].FirstRow;
                var extra = card.Sections[Extra].FirstRow;
                Guid actor = NumberingActor(session, Request.Session.UserSid.TranslateToName());
                Guid author = Id(main, "Author");
                Guid state = Id(sys, "State");
                bool changed = false;
                if (initialize && state == PreparationState && (author == Guid.Empty || AuthorOrDeputy(session, author, actor) || (Id(extra, "Journal") != Guid.Empty && CanEditJournal(c, actor, Id(extra, "Journal")))))
                {
                    if (card.LockStatus == LockStatus.Free)
                    {
                        card.PlaceLock();
                        ownLock = true;
                    }
                    else if (card.LockStatus != LockStatus.OwnerLocked && card.LockStatus != LockStatus.OwnerCheckedOut)
                    {
                        throw new InvalidOperationException("Карточка занята другим пользователем.");
                    }

                    var employee = c.GetObject<StaffEmployee>(author == Guid.Empty ? actor : author);
                    var unit = employee.Unit;
                    Guid journal = Id(extra, "Journal");
                    Guid go = new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e");
                    Guid kazan = new Guid("08137765-5066-4e5d-a189-4f459e32feab");
                    Guid saratov = new Guid("adc70c7d-54d7-4a13-aa6f-2cc208f572b7");
                    if (journal == Guid.Empty)
                    {
                        for (var u = unit; u != null; u = u.ParentUnit)
                        {
                            Guid id = u.GetObjectId();
                            if (id == new Guid("4900f7b1-472c-447d-bc0a-7d460937ceb8"))
                            {
                                journal = go;
                                break;
                            }

                            if (id == new Guid("691c36db-f7da-4497-9f9b-6930083bf85c"))
                            {
                                journal = kazan;
                                break;
                            }

                            if (id == new Guid("617ac655-38d4-4dab-9654-7865dc630b10"))
                            {
                                journal = saratov;
                                break;
                            }
                        }
                    }

                    StaffEmployee manager = null;
                    if (journal == go)
                    {
                        for (var u = unit; u != null; u = u.ParentUnit)
                        {
                            if (u.Manager != null)
                            {
                                manager = u.Manager;
                                break;
                            }
                        }
                    }
                    else if (journal == kazan || journal == saratov)
                    {
                        manager = c.GetObject<StaffUnit>(new Guid("98f2635a-02a4-4af7-92ca-f4bb7674434d")).Manager;
                    }

                    card.BeginUpdate();
                    try
                    {
                        if (author == Guid.Empty)
                        {
                            main.SetGuid("Author", actor);
                            author = actor;
                            changed = true;
                        }

                        if (Id(main, "RegistrationPlaceId") == Guid.Empty && unit != null)
                        {
                            main.SetGuid("RegistrationPlaceId", unit.GetObjectId());
                            changed = true;
                        }

                        if (Id(extra, "Journal") == Guid.Empty && journal != Guid.Empty)
                        {
                            extra.SetGuid("Journal", journal);
                            changed = true;
                        }

                        if (manager != null && Id(extra, "InitiatorManager") != manager.GetObjectId())
                        {
                            extra.SetGuid("InitiatorManager", manager.GetObjectId());
                            changed = true;
                        }

                        if (Id(extra, "DocumentType") == Guid.Empty)
                        {
                            extra.SetGuid("DocumentType", new Guid("620181fa-7a6a-4be9-883e-55f4060160d5"));
                            changed = true;
                        }

                        if (Id(extra, "DeliveryMethod") == Guid.Empty)
                        {
                            extra.SetGuid("DeliveryMethod", new Guid("44a13485-6e98-4775-a394-95f666864830"));
                            changed = true;
                        }

                        if (Id(extra, "SigningMethod") == Guid.Empty)
                        {
                            extra.SetGuid("SigningMethod", new Guid("f0c88d59-f93a-42fd-9da0-ae4d10785470"));
                            changed = true;
                        }

                        card.EndUpdate();
                    }
                    catch
                    {
                        card.CancelUpdate();
                        throw;
                    }
                }

                bool isAuthor = AuthorOrDeputy(session, author, actor);
                Guid journalId = Id(extra, "Journal");
                bool registrar = journalId != Guid.Empty && RegistrarOrDeputy(c, actor, RegistrarGroup(journalId));
                bool journalEditor = CanEditJournal(c, actor, journalId);
                bool control = false;
                Guid history = Id(extra, "ActiveApprovalId");
                if (state == new Guid("b7be72ff-4ede-490d-85db-22ee324e27f4") && history != Guid.Empty)
                {
                    var r = c.GetObject<Reconcile>(history);
                    var tasks = c.GetService<ITaskService>();
                    var stages = c.GetService<IApprovalTaskService>();
                    foreach (var entry in r.MainInfo.TaskList.Tasks)
                    {
                        var t = entry.Task;
                        if (t != null && !tasks.IsTaskCompleted(t) && t.CurrentPerformers.Any(p => p.Employee != null && AuthorizedDeputy(c, p.Employee.GetObjectId(), actor, false)))
                        {
                            var stage = stages.GetTaskApprovalStage(t);
                            if (stage.MainInfo.Mode == ApprovalStageMode.Approval && stage.MainInfo.ReconcileType == ApprovalType.Alternative)
                            {
                                control = true;
                            }
                        }
                    }
                }

                root.SetAttribute("createdAt", card.CreateDate.ToString("o"));
                Guid temporary = Id(main, "RegNumberProvisional");
                root.SetAttribute("temporaryNumber", temporary == Guid.Empty ? "" : card.Sections[NumbersSection].Rows[temporary].GetString("Number"));
                root.SetAttribute("status", "ok");
                root.SetAttribute("initialized", changed ? "true" : "false");
                root.SetAttribute("state", state.ToString());
                root.SetAttribute("author", author.ToString());
                root.SetAttribute("draftEdit", isAuthor && (state == PreparationState || state == new Guid("bcc23707-c0aa-4b2c-bbec-3b6ff751aca5")) ? "true" : "false");
                root.SetAttribute("journalEdit", journalEditor && state == PreparationState ? "true" : "false");
                root.SetAttribute("shortAllowed", actor == author && registrar && state == PreparationState ? "true" : "false");
                root.SetAttribute("controlEdit", control ? "true" : "false");
                root.SetAttribute("shippingEdit", state == RegisteredState && (isAuthor || registrar) ? "true" : "false");
                root.SetAttribute("shortRoute", extra.GetBoolean("ShortRoute").GetValueOrDefault() ? "true" : "false");
                root.SetAttribute("canGenerate", isAuthor && state == PreparationState ? "true" : "false");
                root.SetAttribute("canNumber", (isAuthor || journalEditor) && state == PreparationState ? "true" : "false");
                root.SetAttribute("canCancel", isAuthor && state == PreparationState ? "true" : "false");
                root.SetAttribute("canStart", isAuthor && state == PreparationState ? "true" : "false");
                root.SetAttribute("canConfirmSignature", state == new Guid("d1754352-3891-46fa-9fd5-61cee8844443") && (isAuthor || registrar) ? "true" : "false");
                return xml.OuterXml;
            }
            catch (Exception e)
            {
                Trace("GetOutgoingWebContext: " + e);
                root.InnerText = e.Message;
                return xml.OuterXml;
            }
            finally
            {
                if (ownLock && card != null)
                {
                    card.RemoveLock();
                }
            }
        }
    }
}
