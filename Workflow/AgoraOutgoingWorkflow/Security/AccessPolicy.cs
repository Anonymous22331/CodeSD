using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectManager.Metadata;
using DocsVision.Platform.ObjectManager.SearchModel;
using DocsVision.Platform.ObjectManager.SystemCards;
using DocsVision.Platform.Security.AccessControl;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel;
using DocsVision.ApprovalDesigner.ObjectModel.Services;

namespace Agora.Outgoing.Workflow
{
    internal sealed class AccessPolicy
    {
        private readonly Dictionary<Guid, StaffEmployee> employees = new Dictionary<Guid, StaffEmployee>();
        private readonly Dictionary<Guid, StaffGroup> groups = new Dictionary<Guid, StaffGroup>();
        private readonly HashSet<Guid> seenCards = new HashSet<Guid>();
        private readonly HashSet<Guid> seenFiles = new HashSet<Guid>();
        private readonly string admin = ((SecurityIdentifier)new NTAccount(Environment.MachineName, "Administrator").Translate(typeof(SecurityIdentifier))).Value;
        private readonly string dir = Path.Combine(Runtime.StateRoot, "access-policy");
        private const CardDataRights Read = CardDataRights.Read;
        private const CardDataRights Full = CardDataRights.FullControl;
        private const CardDataRights Modify = Read | CardDataRights.Modify | CardDataRights.PerformOperations;
        private int changed;

        /// <summary>
        /// Преобразует строковое значение в идентификатор объекта.
        /// </summary>
        private static Guid ParseIdentifier(string id)
        {
            return new Guid(id);
        }

        private static readonly Dictionary<Guid, Guid> JournalGroups = new Dictionary<Guid, Guid>
        {
            {
                ParseIdentifier("291adc04-cff2-4a23-bb06-4336c1dbb19e"),
                ParseIdentifier("4adf3c4e-aae8-466e-be68-91eba2c2dc89")
            },
            {
                ParseIdentifier("08137765-5066-4e5d-a189-4f459e32feab"),
                ParseIdentifier("6dc14225-3c84-49f9-aa26-4f4ea5ce9755")
            },
            {
                ParseIdentifier("adc70c7d-54d7-4a13-aa6f-2cc208f572b7"),
                ParseIdentifier("bd966761-ac24-4de3-a6c6-23032b8a6b39")
            }
        };

        /// <summary>
        /// Создаёт базовый набор прав системных администраторов.
        /// </summary>
        private Dictionary<string, CardDataRights> NewRights()
        {
            return new Dictionary<string, CardDataRights>
            {
                {
                    admin,
                    Full
                },
                {
                    "S-1-5-18",
                    Full
                },
                {
                    "S-1-5-32-544",
                    Full
                }
            };
        }

        /// <summary>
        /// Находит сотрудника в подготовленном индексе справочника.
        /// </summary>
        private StaffEmployee Employee(Guid id)
        {
            StaffEmployee e;
            return employees.TryGetValue(id, out e) ? e : null;
        }

        /// <summary>
        /// Индексирует сотрудников подразделений и вложенных подразделений.
        /// </summary>
        private void Index(dynamic units)
        {
            foreach (dynamic unit in units)
            {
                foreach (StaffEmployee e in unit.Employees)
                {
                    employees[e.GetObjectId()] = e;
                }

                Index(unit.Units);
            }
        }

        /// <summary>
        /// Добавляет права сотруднику и его действующим заместителям.
        /// </summary>
        private void Add(Dictionary<string, CardDataRights> map, StaffEmployee employee, CardDataRights rights)
        {
            if (employee == null || String.IsNullOrEmpty(employee.AccountSID))
            {
                return;
            }

            var candidates = new List<StaffEmployee>
            {
                employee
            };
            foreach (var d in employee.Deputies)
            {
                if (d.DeputyAccess && d.Employee != null && d.Employee.Status == StaffEmployeeStatus.Active && !d.Employee.NotAvailable && (d.Permanent || (employee.Status != StaffEmployeeStatus.Active && employee.ActiveEmployee != null && employee.ActiveEmployee.GetObjectId() == d.Employee.GetObjectId())))
                {
                    candidates.Add(d.Employee);
                }
            }

            foreach (var candidate in candidates)
            {
                if (String.IsNullOrEmpty(candidate.AccountSID))
                {
                    continue;
                }

                CardDataRights old;
                map[candidate.AccountSID] = (map.TryGetValue(candidate.AccountSID, out old) ? old : 0) | rights;
            }
        }

        /// <summary>
        /// Предоставляет руководителю сотрудника право чтения.
        /// </summary>
        private void Manager(Dictionary<string, CardDataRights> map, StaffEmployee e)
        {
            if (e != null && e.Unit != null)
            {
                Add(map, e.Unit.Manager, Read);
            }
        }

        /// <summary>
        /// Добавляет права сотрудникам выбранной группы.
        /// </summary>
        private void Group(Dictionary<string, CardDataRights> map, Guid id, CardDataRights rights)
        {
            StaffGroup group;
            if (groups.TryGetValue(id, out group))
            {
                foreach (Guid e in group.UniqueEmployeesIds)
                {
                    Add(map, Employee(e), rights);
                }
            }
        }

        private const AccessControlSections Sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;

        /// <summary>
        /// Применяет права к карточке с сохранением исходного SDDL.
        /// </summary>
        private void Card(Guid id, Dictionary<string, CardDataRights> map)
        {
            if (id == Guid.Empty || !seenCards.Add(id))
            {
                return;
            }

            var raw = Runtime.Session.CardManager.GetCardData(id);
            if (raw.LockStatus != LockStatus.Free)
            {
                return;
            }

            var old = raw.GetAccessControl();
            string sddl = old.GetSecurityDescriptorSddlForm(Sections);
            string backup = Path.Combine(dir, "card-" + id.ToString("N") + ".sddl");
            if (!File.Exists(backup))
            {
                File.WriteAllText(backup, sddl);
            }

            var security = new CardDataSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(new SecurityIdentifier(admin));
            security.SetGroup(new SecurityIdentifier(admin));
            foreach (var e in map.OrderBy(x => x.Key))
            {
                security.AddAccessRule(new CardDataAccessRule(new SecurityIdentifier(e.Key), e.Value, AccessControlType.Allow));
            }

            if (security.GetSecurityDescriptorSddlForm(Sections) != sddl)
            {
                raw.SetAccessControl(security);
                changed++;
            }
        }

        /// <summary>
        /// Применяет права к файлу с сохранением исходного SDDL.
        /// </summary>
        private void FileRights(FileData file, Dictionary<string, CardDataRights> map)
        {
            if (file == null || !seenFiles.Add(file.Id))
            {
                return;
            }

            var old = file.GetAccessControl();
            string sddl = old.GetSecurityDescriptorSddlForm(Sections);
            string backup = Path.Combine(dir, "file-" + file.Id.ToString("N") + ".sddl");
            if (!File.Exists(backup))
            {
                File.WriteAllText(backup, sddl);
            }

            var security = new FileDataSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(new SecurityIdentifier(admin));
            security.SetGroup(new SecurityIdentifier(admin));
            foreach (var e in map.OrderBy(x => x.Key))
            {
                security.AddAccessRule(new FileDataAccessRule(new SecurityIdentifier(e.Key), e.Value == Full ? FileDataRights.FullControl : FileDataRights.Read, AccessControlType.Allow));
            }

            if (security.GetSecurityDescriptorSddlForm(Sections) != sddl)
            {
                file.SetAccessControl(security);
                changed++;
            }
        }

        /// <summary>
        /// Применяет права к карточке версионного файла и его версиям.
        /// </summary>
        private void Versioned(Guid id, Dictionary<string, CardDataRights> map)
        {
            if (id == Guid.Empty)
            {
                return;
            }

            var root = new Dictionary<string, CardDataRights>(map);
            foreach (string sid in root.Keys.ToArray())
            {
                if (root[sid] != Full)
                {
                    root[sid] &= ~CardDataRights.DeleteChildObjects;
                }
            }

            Card(id, root);
            var vf = (VersionedFileCard)Runtime.Session.CardManager.GetCard(id);
            foreach (var version in vf.Versions)
            {
                FileRights(version, map);
            }
        }

        /// <summary>
        /// Обновляет права документов, согласований и связанных файлов.
        /// </summary>
        public void Apply()
        {
            Directory.CreateDirectory(dir);
            var context = Runtime.Context;
            var session = Runtime.Session;
            var staff = context.GetObject<Staff>(ParseIdentifier("6710b92a-e148-4363-8a6f-1aa0eb18936c"));
            Index(staff.Units);
            var groupRoot = staff.Groups.Single(g => g.Name == "АГОРА — Исходящие письма");
            foreach (var group in groupRoot.Groups)
            {
                groups[group.GetObjectId()] = group;
            }

            var definitions = NewRights();
            foreach (var e in employees.Values)
            {
                var visited = new HashSet<Guid>();
                for (var unit = e.Unit; unit != null && visited.Add(unit.GetObjectId()); unit = unit.ParentUnit)
                {
                    if (new[]
                    {
                        ParseIdentifier("4900f7b1-472c-447d-bc0a-7d460937ceb8"),
                        ParseIdentifier("691c36db-f7da-4497-9f9b-6930083bf85c"),
                        ParseIdentifier("617ac655-38d4-4dab-9654-7865dc630b10")
                    }.Contains(unit.GetObjectId()))
                    {
                        Add(definitions, e, Read | CardDataRights.PerformOperations);
                        break;
                    }
                }
            }

            var config = Runtime.Read<Dictionary<string, object>>(Path.Combine(Runtime.Root, "approval-config.json"));
            foreach (string key in new[]
            {
                "paths",
                "stages"
            }

            )
            {
                foreach (var entry in (Dictionary<string, object>)config[key])
                {
                    Card(ParseIdentifier((string)entry.Value), definitions);
                }
            }

            foreach (string name in new[]
            {
                "assignment-config.xml",
                "registration-config.xml"
            }

            )
            {
                var xml = new XmlDocument
                {
                    XmlResolver = null
                };
                xml.Load(Path.Combine(Runtime.Root, @"server\" + name));
                foreach (XmlElement node in xml.SelectNodes("//Counter"))
                {
                    Guid id = ParseIdentifier(node.GetAttribute("id"));
                    var counter = (NumeratorCard)session.CardManager.GetCard(id);
                    int year = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time")).Year;
                    foreach (int y in new[]
                    {
                        year,
                        year + 1
                    }

                    )
                    {
                        if (!counter.Zones.Any(z => z.Name == "Y" + y))
                        {
                            counter.Zones.AddNew("Y" + y);
                        }
                    }

                    Card(id, definitions);
                }
            }

            var taskService = context.GetService<ITaskService>();
            var stageService = context.GetService<IApprovalTaskService>();
            var failures = new List<object>();
            foreach (CardData raw in session.CardManager.FindCards(Runtime.Query(null).GetXml()))
            {
                try
                {
                    ApplyDocument(raw, taskService, stageService);
                }
                catch (Exception ex)
                {
                    failures.Add(new { card = raw.Id, error = ex.ToString() });
                }
            }

            Runtime.Save(Path.Combine(dir, "last-run.json"), new { status = failures.Count == 0 ? "applied" : "partial", changed = changed, failures = failures, implementation = "C#", utc = DateTime.UtcNow.ToString("o") });
            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Access policy failed cards: " + failures.Count);
            }
        }

        public sealed class ParticipantRecord
        {
            public string[] employees;
        }

        /// <summary>
        /// Обновляет права исходящего письма и связанных объектов.
        /// </summary>
        private void ApplyDocument(CardData raw, ITaskService taskService, IApprovalTaskService stageService)
        {
            var context = Runtime.Context;
            var session = Runtime.Session;
            var sys = raw.Sections[Runtime.SystemSection].FirstRow;
            var main = raw.Sections[Runtime.MainSection].FirstRow;
            var extra = raw.Sections[Runtime.ExtraSection].FirstRow;
            Guid state = Runtime.Id(sys, "State");
            bool draft = state == ParseIdentifier("ab48f98d-3637-48aa-92bd-556bc88475c6");
            var map = NewRights();
            var author = Employee(Runtime.Id(main, "Author"));
            CardDataRights authorRights = draft || state == ParseIdentifier("bcc23707-c0aa-4b2c-bbec-3b6ff751aca5") || state == ParseIdentifier("11b0eb5d-3fa1-48a9-ba0b-e9ed1c2b1510") ? Modify : Read;
            var doc = context.GetObject<Document>(raw.Id);
            if (draft && (doc.MainInfo.SignatureList == null || doc.MainInfo.SignatureList.Signatures.Count == 0))
            {
                authorRights |= CardDataRights.Delete;
            }

            Add(map, author, authorRights);
            Group(map, ParseIdentifier("942df52e-50b6-4d67-b537-b82c46ade308"), Full);
            Guid journalGroup;
            bool hasGroup = JournalGroups.TryGetValue(Runtime.Id(extra, "Journal"), out journalGroup);
            if (draft)
            {
                Group(map, ParseIdentifier("4adf3c4e-aae8-466e-be68-91eba2c2dc89"), Modify);
                if (hasGroup)
                {
                    Group(map, journalGroup, Modify);
                }
            }
            else
            {
                Manager(map, author);
                Add(map, Employee(Runtime.Id(extra, "InitiatorManager")), Read);
                foreach (var person in new[]
                {
                    new[]
                    {
                        "d9f3bb4c-9c1a-464c-90f3-3d9657864709",
                        "Signer"
                    },
                    new[]
                    {
                        "281a97ff-667f-46c8-8fbe-7cfc02edfedb",
                        "Approver"
                    }
                }

                )
                {
                    foreach (RowData row in raw.Sections[ParseIdentifier(person[0])].Rows)
                    {
                        var e = Employee(Runtime.Id(row, person[1]));
                        Manager(map, e);
                        Add(map, e, Read);
                    }
                }

                if (hasGroup)
                {
                    Group(map, journalGroup, state == ParseIdentifier("11b0eb5d-3fa1-48a9-ba0b-e9ed1c2b1510") ? Modify : Read);
                }

                Group(map, ParseIdentifier("483fe3ce-a4d9-41c9-a00d-e559ceaee982"), Read);
                Group(map, ParseIdentifier("fa66bc1f-0da1-4391-8209-80b19d0c6040"), Read);
            }

            Reconcile reconcile = null;
            var writers = new List<StaffEmployee>();
            var tasks = new List<DocsVision.BackOffice.ObjectModel.Task>();
            Guid history = Runtime.Id(extra, "ActiveApprovalId");
            if (history != Guid.Empty)
            {
                reconcile = context.GetObject<Reconcile>(history);
                if (reconcile.MainInfo.Document == null || reconcile.MainInfo.Document.GetObjectId() != raw.Id)
                {
                    throw new InvalidOperationException("Approval belongs to different document");
                }

                foreach (var entry in reconcile.MainInfo.TaskList.Tasks)
                {
                    var task = entry.Task;
                    if (task == null)
                    {
                        continue;
                    }

                    tasks.Add(task);
                    Add(map, task.MainInfo.CompletedUser, Read);
                    foreach (var p in task.CurrentPerformers)
                    {
                        Add(map, p.Employee, Read);
                        if (!taskService.IsTaskCompleted(task))
                        {
                            var stage = stageService.GetTaskApprovalStage(task);
                            if (stage.MainInfo.Mode == ApprovalStageMode.Signing || stage.MainInfo.Mode == ApprovalStageMode.Consolidation || stage.MainInfo.ReconcileType == ApprovalType.Alternative)
                            {
                                writers.Add(p.Employee);
                                if (stage.MainInfo.ReconcileType == ApprovalType.Alternative)
                                {
                                    Add(map, p.Employee, Modify);
                                }
                            }
                        }
                    }
                }
            }

            var auxQuery = session.CreateSearchQuery();
            var auxSection = auxQuery.AttributiveSearch.CardTypeQueries.AddNew(ParseIdentifier("c7b36f33-cdd4-4da9-8444-600fe14111e4")).SectionQueries.AddNew(ParseIdentifier("20d21193-9f7f-4b62-8d69-272e78e1d6a8"));
            auxSection.ConditionGroup.Conditions.AddNew("ParentDocument", FieldType.RefId, ConditionOperation.Equals, raw.Id);
            var trusted = new List<DocsVision.BackOffice.ObjectModel.Task>();
            foreach (CardData auxRaw in session.CardManager.FindCards(auxQuery.GetXml()))
            {
                if (!tasks.Any(t => t.GetObjectId() == auxRaw.Id))
                {
                    string record = Path.Combine(Runtime.StateRoot, @"trusted-auxiliary\" + auxRaw.Id.ToString("N") + ".xml");
                    if (!File.Exists(record))
                    {
                        continue;
                    }

                    var proof = new XmlDocument
                    {
                        XmlResolver = null
                    };
                    proof.Load(record);
                    var root = proof.DocumentElement;
                    if (root.Name != "TrustedAuxiliaryTask" || ParseIdentifier(root.GetAttribute("document")) != raw.Id || ParseIdentifier(root.GetAttribute("task")) != auxRaw.Id)
                    {
                        continue;
                    }

                    var aux = context.GetObject<DocsVision.BackOffice.ObjectModel.Task>(auxRaw.Id);
                    if (aux.MainInfo.Author == null || aux.MainInfo.Author.GetObjectId() != ParseIdentifier(root.GetAttribute("author")) || aux.SystemInfo.CardKind.GetObjectId() != ParseIdentifier(root.GetAttribute("kind")))
                    {
                        continue;
                    }

                    tasks.Add(aux);
                    trusted.Add(aux);
                }
            }

            string participants = Path.Combine(dir, "participants-" + raw.Id.ToString("N") + ".json");
            var people = LoadParticipants(raw, participants);
            foreach (var task in tasks)
            {
                if (task.MainInfo.CompletedUser != null)
                {
                    people.Add(task.MainInfo.CompletedUser.GetObjectId());
                }

                foreach (var p in task.CurrentPerformers)
                {
                    if (p.Employee != null)
                    {
                        people.Add(p.Employee.GetObjectId());
                    }
                }
            }

            foreach (Guid id in people)
            {
                Add(map, Employee(id), Read);
            }

            Runtime.Save(participants, new ParticipantRecord { employees = people.Select(x => x.ToString()).OrderBy(x => x).ToArray() });
            if (raw.LockStatus == LockStatus.Free)
            {
                var index = raw.Sections[ParseIdentifier("4c497917-8a21-4c33-a27a-b95113b530b0")];
                var old = index.Rows.Cast<RowData>().Select(r => Runtime.Id(r, "Employee")).OrderBy(x => x);
                if (!old.SequenceEqual(people.OrderBy(x => x)))
                {
                    raw.PlaceLock();
                    try
                    {
                        raw.BeginUpdate();
                        try
                        {
                            foreach (var row in index.Rows.Cast<RowData>().ToArray())
                            {
                                index.Rows.Remove(row.Id);
                            }

                            foreach (Guid id in people)
                            {
                                index.Rows.AddNew().SetGuid("Employee", id);
                            }

                            raw.EndUpdate();
                        }
                        catch
                        {
                            raw.CancelUpdate();
                            throw;
                        }
                    }
                    finally
                    {
                        raw.RemoveLock();
                    }
                }
            }

            Card(raw.Id, map);
            if (trusted.Count > 0 && raw.LockStatus == LockStatus.Free)
            {
                var lists = context.GetService<ITaskListService>();
                var list = doc.MainInfo.Tasks;
                if (list == null)
                {
                    list = lists.CreateTaskList();
                    context.SaveObject(list);
                    doc.MainInfo.Tasks = list;
                    context.SaveObject(doc);
                }

                foreach (var aux in trusted)
                {
                    if (!list.Tasks.Any(t => t.Task != null && t.Task.GetObjectId() == aux.GetObjectId()))
                    {
                        lists.AddTask(list, aux, doc);
                    }
                }

                context.SaveObject(list);
                context.AcceptChanges();
                Card(list.GetObjectId(), map);
            }

            ApplyDocumentFiles(doc, map);
            if (reconcile != null)
            {
                ApplyApprovalGraph(reconcile, map, writers, tasks);
            }
        }

        /// <summary>
        /// Применяет права к файлам, ссылкам и подписям документа.
        /// </summary>
        private void ApplyDocumentFiles(Document doc, Dictionary<string, CardDataRights> map)
        {
            var session = Runtime.Session;
            foreach (var file in doc.Files)
            {
                if (file.FileType == DocumentFileType.Main)
                {
                    Versioned(file.FileId, map);
                }
                else
                {
                    FileRights(session.FileManager.GetFile(file.FileId), map);
                }
            }

            if (doc.MainInfo.ReferenceList != null)
            {
                Card(doc.MainInfo.ReferenceList.GetObjectId(), map);
            }

            if (doc.MainInfo.SignatureList != null)
            {
                Card(doc.MainInfo.SignatureList.GetObjectId(), map);
                foreach (var signature in doc.MainInfo.SignatureList.Signatures)
                {
                    foreach (var part in signature.Parts)
                    {
                        if (part.Signature != Guid.Empty)
                        {
                            FileRights(session.FileManager.GetFile(part.Signature), map);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Читает участников из карточки и восстанавливает повреждённый служебный кэш.
        /// </summary>
        private HashSet<Guid> LoadParticipants(CardData document, string cachePath)
        {
            var section = document.Sections[ParseIdentifier("4c497917-8a21-4c33-a27a-b95113b530b0")];
            var people = new HashSet<Guid>(section.Rows.Cast<RowData>().Select(row => Runtime.Id(row, "Employee")).Where(id => id != Guid.Empty));
            if (!File.Exists(cachePath))
            {
                return people;
            }

            try
            {
                var stored = Runtime.Read<ParticipantRecord>(cachePath);
                if (stored == null)
                {
                    throw new FormatException("Кэш участников не содержит записи.");
                }

                if (stored.employees != null)
                {
                    foreach (var id in stored.employees)
                    {
                        people.Add(ParseIdentifier(id));
                    }
                }
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is FormatException) && !(error is InvalidOperationException))
                {
                    throw;
                }

                var backup = cachePath + ".corrupt-" + Guid.NewGuid().ToString("N");
                File.Copy(cachePath, backup);
                Runtime.Log("access", new { status = "participants_cache_recovered_from_card", card = document.Id, backup = backup, error = error.Message });
            }

            return people;
        }

        /// <summary>
        /// Применяет права к согласованию, маршруту и заданиям.
        /// </summary>
        private void ApplyApprovalGraph(Reconcile reconcile, Dictionary<string, CardDataRights> map, List<StaffEmployee> writers, List<DocsVision.BackOffice.ObjectModel.Task> tasks)
        {
            var context = Runtime.Context;
            var taskService = context.GetService<ITaskService>();
            var session = Runtime.Session;
            var graph = new Dictionary<string, CardDataRights>(map);
            foreach (string sid in graph.Keys.ToArray())
            {
                if (graph[sid] != Full)
                {
                    graph[sid] = Read;
                }
            }

            foreach (var writer in writers)
            {
                Add(graph, writer, Modify);
            }

            Card(reconcile.GetObjectId(), graph);
            Card(reconcile.MainInfo.TaskList.GetObjectId(), map);
            Card(reconcile.MainInfo.ReconcileFileList.GetObjectId(), graph);
            foreach (var file in reconcile.MainInfo.ReconcileFileList.Files)
            {
                if (file.HardFileId != Guid.Empty)
                {
                    FileRights(session.FileManager.GetFile(file.HardFileId), graph);
                }
                else
                {
                    Versioned(file.FileId, graph);
                }
            }

            var path = context.GetObject<ApprovalPath>(reconcile.MainInfo.PathId);
            Card(path.GetObjectId(), map);
            foreach (var road in path.RoadMaps)
            {
                Card(road.Stage.GetObjectId(), map);
            }

            foreach (var record in path.StagesRecords)
            {
                Card(record.Stage.GetObjectId(), map);
            }

            foreach (var task in tasks)
            {
                var taskMap = new Dictionary<string, CardDataRights>(map);
                foreach (string sid in taskMap.Keys.ToArray())
                {
                    if (taskMap[sid] != Full)
                    {
                        taskMap[sid] = Read;
                    }
                }

                if (!taskService.IsTaskCompleted(task))
                {
                    foreach (var p in task.CurrentPerformers)
                    {
                        Add(taskMap, p.Employee, Modify);
                    }
                }

                Card(task.GetObjectId(), taskMap);
            }
        }
    }
}
