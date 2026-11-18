using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using System.Web.Script.Serialization;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectManager.Metadata;
using DocsVision.Platform.ObjectManager.SearchModel;
using DocsVision.Platform.ObjectModel;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel.Mapping;
using DocsVision.Platform.ObjectModel.Mapping;
using Agora.Outgoing.Deployment;

namespace Agora.Outgoing.Workflow
{
    public static partial class Runtime
    {
        private static readonly object Gate = new object ();
        internal const string Root = @"C:\AGORA-OUT";
        internal const string StateRoot = @"C:\ProgramData\Docsvision\AGORA-OUT";
        internal static readonly Guid Kind = new Guid("7cf57200-ae68-4bca-ba7f-2243a9f783bc");
        internal static readonly Guid SystemSection = new Guid("91b2c5f7-9324-4cef-9afe-a457c8310f06");
        internal static readonly Guid ExtraSection = new Guid("a60f4e32-03be-4d47-810a-a595ed51796d");
        internal static readonly Guid MainSection = new Guid("30eb9b87-822b-4753-9a50-a1825dca1b74");
        internal static ObjectContext Context
        {
            get
            {
                return ContextConnection.Context;
            }
        }

        internal static UserSession Session
        {
            get
            {
                return ContextConnection.Session;
            }
        }

        internal static JavaScriptSerializer Json
        {
            get
            {
                return new JavaScriptSerializer();
            }
        }

        /// <summary>
        /// Атомарно сохраняет служебные данные в JSON-файл.
        /// </summary>
        internal static void Save(string file, object data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(data), new System.Text.UTF8Encoding(false));
            if (File.Exists(file))
            {
                File.Replace(tmp, file, null);
            }
            else
            {
                File.Move(tmp, file);
            }
        }

        /// <summary>
        /// Загружает служебные данные из JSON-файла.
        /// </summary>
        internal static T Read<T>(string file)
        {
            return Json.Deserialize<T>(File.ReadAllText(file));
        }

        /// <summary>
        /// Добавляет событие обработки в диагностический журнал.
        /// </summary>
        internal static void Log(string mode, object entry)
        {
            string dir = Path.Combine(StateRoot, "csharp-runtime");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, mode + ".jsonl"), Json.Serialize(new { utc = DateTime.UtcNow.ToString("o"), data = entry }) + Environment.NewLine);
        }

        /// <summary>
        /// Читает идентификатор связанного объекта из строки карточки.
        /// </summary>
        internal static Guid Id(RowData row, string field)
        {
            return row.GetGuid(field).GetValueOrDefault();
        }

        /// <summary>
        /// Создаёт поисковый запрос исходящих писем с фильтром состояния.
        /// </summary>
        internal static SearchQuery Query(Guid? state)
        {
            var q = Session.CreateSearchQuery();
            var ct = q.AttributiveSearch.CardTypeQueries.AddNew(new Guid("b9f7bfd7-7429-455e-a3f1-94ffb569c794"));
            ct.SectionQueryGroup.Operation = SectionQueryOperation.And;
            var sys = ct.SectionQueryGroup.SectionQueries.AddNew(SystemSection);
            sys.ConditionGroup.Operation = ConditionGroupOperation.And;
            sys.ConditionGroup.Conditions.AddNew("Kind", FieldType.RefId, ConditionOperation.Equals, Kind);
            if (state.HasValue)
            {
                sys.ConditionGroup.Conditions.AddNew("State", FieldType.RefId, ConditionOperation.Equals, state.Value);
            }

            return q;
        }

        /// <summary>
        /// Вызывает метод серверного расширения и проверяет результат.
        /// </summary>
        internal static XmlDocument Call(string name, params KeyValuePair<string, Guid>[] args)
        {
            var method = Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation", name);
            foreach (var a in args)
            {
                method.Parameters.AddNew(a.Key, ParameterValueType.Guid, a.Value);
            }

            var xml = new XmlDocument();
            xml.XmlResolver = null;
            xml.LoadXml((string)method.Execute());
            if (xml.DocumentElement.GetAttribute("status") == "error")
            {
                throw new InvalidOperationException(xml.DocumentElement.InnerText);
            }

            return xml;
        }

        /// <summary>
        /// Создаёт именованный параметр вызова серверного расширения.
        /// </summary>
        internal static KeyValuePair<string, Guid> Arg(string name, Guid value)
        {
            return new KeyValuePair<string, Guid>(name, value);
        }

        /// <summary>
        /// Выполняет обработчик с блокировкой повторного запуска.
        /// </summary>
        public static void Run(string mode)
        {
            lock (Gate)
            {
                using (var mutex = new Mutex(false, @"Global\AgoraOutgoingCSharp-" + mode))
                {
                    bool held = false;
                    try
                    {
                        try
                        {
                            held = mutex.WaitOne(0);
                        }
                        catch (AbandonedMutexException)
                        {
                            held = true;
                        }

                        if (!held)
                        {
                            return;
                        }

                        if (mode == "crl")
                        {
                            RefreshCrl();
                            return;
                        }

                        ContextConnection.Open("dv002_exam");
                        Context.GetService<IObjectMapperFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerMapperFactory));
                        Context.GetService<IServiceFactoryRegistry>().RegisterFactory(typeof(ApprovalDesignerServiceFactory));
                        if (mode == "delivery")
                        {
                            RegisterPending();
                            Notify();
                        }
                        else if (mode == "archive")
                        {
                            Archive();
                        }
                        else if (mode == "access")
                        {
                            new AccessPolicy().Apply();
                        }
                        else if (mode == "notifications")
                        {
                            Notify();
                        }
                        else if (mode == "registration")
                        {
                            RegisterPending();
                        }
                        else
                        {
                            throw new ArgumentException("Unknown mode: " + mode);
                        }

                        Log(mode, new { status = "completed" });
                    }
                    catch (Exception ex)
                    {
                        Log(mode, new { status = "error", error = ex.ToString() });
                        throw;
                    }
                    finally
                    {
                        try
                        {
                            if (ContextConnection.Session != null)
                            {
                                ContextConnection.Close();
                            }
                        }
                        finally
                        {
                            if (held)
                            {
                                mutex.ReleaseMutex();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Архивирует отправленные письма по календарному сроку в МСК.
        /// </summary>
        private static void Archive()
        {
            var sent = new Guid("75fe7ede-fbaa-4c32-b3eb-16e1e7b99b9f");
            var archive = new Guid("cc5c57be-c7de-4d72-84cb-244ab0efce39");
            var cutoff = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time")).Date.AddDays(-10);
            var states = Context.GetService<IStateService>();
            var setting = states.GetKindStateSetting(Context.GetObject<KindsCardKind>(Kind));
            var branch = setting.StateMachineBranches.Single(b => b.StartState != null && b.EndState != null && b.Operation != null && b.StartState.GetObjectId() == sent && b.EndState.GetObjectId() == archive && b.Operation.DefaultName == "AGORA.Delivery.Archive");
            int failed = 0;
            foreach (CardData card in Session.CardManager.FindCards(Query(sent).GetXml()))
            {
                bool locked = false;
                try
                {
                    if (card.LockStatus != LockStatus.Free)
                    {
                        continue;
                    }

                    card.PlaceLock();
                    locked = true;
                    card.Refresh();
                    var date = card.Sections[ExtraSection].FirstRow.GetDateTime("ActualSendingDate");
                    if (Id(card.Sections[SystemSection].FirstRow, "Kind") != Kind || Id(card.Sections[SystemSection].FirstRow, "State") != sent || !date.HasValue || date.Value.Date > cutoff)
                    {
                        continue;
                    }

                    var doc = Context.GetObject<Document>(card.Id);
                    Context.RefreshObject(doc);
                    states.ChangeState(doc, branch);
                    Context.SaveObject(doc);
                    Context.AcceptChanges();
                    Log("archive", new { status = "archived", card = card.Id, cutoff = cutoff.ToString("yyyy-MM-dd") });
                }
                catch (Exception ex)
                {
                    failed++;
                    Context.RollbackChanges();
                    Log("archive", new { card = card.Id, error = ex.ToString() });
                }
                finally
                {
                    if (locked)
                    {
                        card.RemoveLock();
                    }
                }
            }

            if (failed > 0)
            {
                throw new InvalidOperationException("Archive failed cards: " + failed);
            }
        }
    }
}
