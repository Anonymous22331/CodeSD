using System;
using System.Linq;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.ObjectModel.Mapping;
using DocsVision.Platform.SystemCards.ObjectModel.Services;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel;
using DocsVision.ApprovalDesigner.ObjectModel.Mapping;
using DocsVision.ApprovalDesigner.ObjectModel.Services;
using DocsVision.Platform.StorageServer.Extensibility;

namespace Agora.Outgoing.Server
{
    public sealed partial class ValidationExtension
    {
        /// <summary>
        /// Создаёт контекст с сервисами согласования Docsvision.
        /// </summary>
        private static ObjectContext ApprovalContext(UserSession session)
        {
            var c = DocumentContext(session);
            c.GetService<IObjectMapperFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerMapperFactory));
            c.GetService<IServiceFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerServiceFactory));
            c.GetService<IObjectMapperFactoryRegistry>().RegisterFactory(typeof(Docsvision.WorkerService.ObjectModel.Mapping.WorkerServiceMapperFactory));
            c.GetService<IServiceFactoryRegistry>().RegisterFactory(typeof(Docsvision.WorkerService.ObjectModel.Services.WorkerServiceServiceFactory));
            return c;
        }

        /// <summary>
        /// Возвращает доступные решения по заданию согласования.
        /// </summary>
        [ExtensionMethod]
        public string GetOutgoingTaskActions(Guid cardId)
        {
            var xml = new XmlDocument();
            var root = xml.CreateElement("TaskActions");
            xml.AppendChild(root);
            root.SetAttribute("status", "ok");
            try
            {
                var session = CreateSession(true);
                var card = session.CardManager.GetCardData(cardId);
                if (card.Type.Id != DocumentType || Id(card.Sections[SystemSection].FirstRow, "Kind") != Kind)
                {
                    throw new InvalidOperationException("Неподдерживаемый документ.");
                }

                Guid historyId = Id(card.Sections[Extra].FirstRow, "ActiveApprovalId");
                if (historyId == Guid.Empty)
                {
                    return xml.OuterXml;
                }

                var c = ApprovalContext(session);
                var r = c.GetObject<Reconcile>(historyId);
                if (r.MainInfo.Document == null || r.MainInfo.Document.GetObjectId() != cardId)
                {
                    throw new InvalidOperationException("Нарушена связь с согласованием.");
                }

                Guid actor = NumberingActor(session, Request.Session.UserSid.TranslateToName());
                var tasks = c.GetService<ITaskService>();
                var stages = c.GetService<IApprovalTaskService>();
                foreach (var taskEntry in r.MainInfo.TaskList.Tasks)
                {
                    var task = taskEntry.Task;
                    if (task == null)
                    {
                        continue;
                    }

                    if (tasks.IsTaskCompleted(task) || !task.CurrentPerformers.Any(p => p.Employee != null && AuthorizedDeputy(c, p.Employee.GetObjectId(), actor, false)))
                    {
                        continue;
                    }

                    var stage = stages.GetTaskApprovalStage(task);
                    var node = xml.CreateElement("Task");
                    root.AppendChild(node);
                    node.SetAttribute("id", task.GetObjectId().ToString());
                    node.SetAttribute("name", stage.MainInfo.Name);
                    node.SetAttribute("signing", stage.MainInfo.Mode == ApprovalStageMode.Signing ? "true" : "false");
                    node.SetAttribute("normalControl", stage.MainInfo.Mode == ApprovalStageMode.Approval && stage.MainInfo.ReconcileType == ApprovalType.Alternative ? "true" : "false");
                    foreach (var decision in stage.Decisions)
                    {
                        if (stage.MainInfo.Mode == ApprovalStageMode.Signing && decision.Semantics != DecisionSemantics.Negative)
                        {
                            continue;
                        }

                        var d = xml.CreateElement("Decision");
                        node.AppendChild(d);
                        d.SetAttribute("semantic", ((int)decision.Semantics).ToString());
                        d.SetAttribute("name", decision.Name);
                        d.SetAttribute("save", (decision.Semantics == DecisionSemantics.Cancellation || decision.Semantics == DecisionSemantics.Negative) ? "false" : "true");
                        d.SetAttribute("comment", decision.RequestComments.GetValueOrDefault() || decision.Semantics == DecisionSemantics.Negative ? "true" : "false");
                    }
                }
            }
            catch (Exception ex)
            {
                root.SetAttribute("status", "error");
                root.InnerText = ex.Message;
                Trace("GetOutgoingTaskActions: " + ex.ToString());
            }

            return xml.OuterXml;
        }

        /// <summary>
        /// Завершает задание с проверкой исполнителя и выбранного решения.
        /// </summary>
        [ExtensionMethod]
        public string CompleteOutgoingTask(Guid taskId, int semantic, string comment)
        {
            try
            {
                var session = CreateSession(true);
                var raw = session.CardManager.GetCardData(taskId);
                var link = raw.Sections[new Guid("03c27047-fd44-4d3d-b4cb-abbf21a76e4c")].FirstRow;
                if (link == null)
                {
                    throw new InvalidOperationException("Это не задание согласования.");
                }

                var c = ApprovalContext(session);
                var r = c.GetObject<Reconcile>(Id(link, "Reconciliation"));
                if (r.MainInfo.Document == null)
                {
                    throw new InvalidOperationException("Не определён документ согласования.");
                }

                var doc = session.CardManager.GetCardData(r.MainInfo.Document.GetObjectId());
                if (doc.Type.Id != DocumentType || Id(doc.Sections[SystemSection].FirstRow, "Kind") != Kind || Id(doc.Sections[Extra].FirstRow, "ActiveApprovalId") != r.GetObjectId())
                {
                    throw new InvalidOperationException("Задание не относится к текущему согласованию исходящего письма АГОРЫ.");
                }

                Guid actor = NumberingActor(session, Request.Session.UserSid.TranslateToName());
                var task = c.GetObject<Task>(taskId);
                var tasks = c.GetService<ITaskService>();
                if (tasks.IsTaskCompleted(task))
                {
                    if (task.MainInfo.CompletedUser != null && task.MainInfo.CompletedUser.GetObjectId() == actor)
                    {
                        return ApprovalReply("unchanged", r.GetObjectId(), "Задание уже завершено.");
                    }

                    throw new InvalidOperationException("Задание уже закрыто другим исполнителем.");
                }

                if (!task.CurrentPerformers.Any(p => p.Employee != null && AuthorizedDeputy(c, p.Employee.GetObjectId(), actor, false)))
                {
                    throw new UnauthorizedAccessException("Решение может принять текущий исполнитель задания.");
                }

                var stage = c.GetService<IApprovalTaskService>().GetTaskApprovalStage(task);
                var value = (DecisionSemantics)semantic;
                if (stage.MainInfo.Mode == ApprovalStageMode.Signing && value != DecisionSemantics.Negative)
                {
                    throw new InvalidOperationException("Подписание выполняется в штатном задании с сертификатом ЭП.");
                }

                var choices = stage.Decisions.Where(d => d.Semantics == value).ToArray();
                if (choices.Length != 1)
                {
                    throw new InvalidOperationException("Решение недоступно на этом этапе.");
                }

                if ((choices[0].RequestComments.GetValueOrDefault() || value == DecisionSemantics.Negative) && String.IsNullOrWhiteSpace(comment))
                {
                    throw new InvalidOperationException("Укажите комментарий к решению.");
                }

                if (value == DecisionSemantics.NewCycle)
                {
                    RefreshStoredManager(session, c, doc);
                }

                if (value == DecisionSemantics.NewCycle || (value == DecisionSemantics.Positive && stage.MainInfo.ReconcileType == ApprovalType.Alternative))
                {
                    var validation = new XmlDocument();
                    validation.LoadXml(Validate(doc.Id, true));
                    if (validation.DocumentElement.GetAttribute("valid") != "true")
                    {
                        throw new InvalidOperationException(validation.DocumentElement.InnerText);
                    }
                }

                if (value == DecisionSemantics.NewCycle)
                {
                    var extra = doc.Sections[Extra].FirstRow;
                    var path = c.GetObject<ApprovalPath>(r.MainInfo.PathId);
                    Guid signer = doc.Sections[new Guid("d9f3bb4c-9c1a-464c-90f3-3d9657864709")].Rows.Cast<RowData>().Select(row => Id(row, "Signer")).FirstOrDefault(id => id != Guid.Empty);
                    bool additional = doc.Sections[new Guid("281a97ff-667f-46c8-8fbe-7cfc02edfedb")].Rows.Cast<RowData>().Any(row => Id(row, "Approver") != Guid.Empty);
                    foreach (var road in path.RoadMaps)
                    {
                        string field = road.Stage.MainInfo.ApproversField ?? "";
                        if (field.EndsWith(@"\InitiatorManager", StringComparison.OrdinalIgnoreCase))
                        {
                            road.Excluded = Id(extra, "InitiatorManager") == signer;
                            road.Stage.MainInfo.DefaultDecision = road.Excluded ? DecisionSemantics.Positive : DecisionSemantics.Negative;
                            c.SaveObject(road.Stage);
                        }
                        else if (field.EndsWith(@"\Approver", StringComparison.OrdinalIgnoreCase))
                        {
                            road.Excluded = !additional;
                        }

                        c.SaveObject(road);
                    }

                    c.SaveObject(path);
                }

                var options = task.Preset.Completion.CompletionOptions.Where(o => o.Name == choices[0].Name).ToArray();
                if (options.Length != 1)
                {
                    throw new InvalidOperationException("Не настроен вариант завершения задания.");
                }

                task.MainInfo.CompletionResult = options[0];
                StatesStateMachineBranch branch;
                if (!tasks.CanCompleteTaskByCompletionParameter(task, out branch))
                {
                    throw new InvalidOperationException("Не найден штатный переход завершения задания.");
                }

                string message;
                if (!tasks.CanCompleteTask(task, tasks.GetKindSettings(task.SystemInfo.CardKind), out message))
                {
                    throw new InvalidOperationException(message);
                }

                link.SetString("Comment", comment ?? "");
                tasks.CompleteTask(task, comment ?? "");
                c.SaveObject(task);
                c.GetService<IStateService>().ChangeState(task, branch);
                c.SaveObject(task);
                c.GetService<IEventService>().FlushEvents(task, true);
                TriggerAccessRefresh();
                Trace("CompleteOutgoingTask task=" + taskId + " actor=" + actor + " decision=" + semantic);
                return ApprovalReply("completed", r.GetObjectId(), "Решение принято.");
            }
            catch (Exception ex)
            {
                Trace("CompleteOutgoingTask: " + ex.ToString());
                return ApprovalReply("error", Guid.Empty, ex.Message);
            }
        }
    }
}
