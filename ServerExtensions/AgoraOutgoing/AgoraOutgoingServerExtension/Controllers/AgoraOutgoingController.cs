using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using DocsVision.Platform.WebClient;
using DocsVision.WebClient.Extensibility;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.WebClientLibrary.ObjectModel.SolutionsDirectory.Export;

namespace Agora.Outgoing.Web
{
    public sealed class AgoraOutgoingController : Controller
    {
        private readonly ICurrentObjectContextProvider provider;
        /// <summary>
        /// Создаёт контроллер операций исходящего письма.
        /// </summary>
        public AgoraOutgoingController(ICurrentObjectContextProvider provider)
        {
            this.provider = provider;
        }

        /// <summary>
        /// Возвращает контекст текущей сессии Web-клиента.
        /// </summary>
        private SessionContext Context()
        {
            if (Request.Headers["X-Agora-Web"] != "1")
            {
                throw new UnauthorizedAccessException("Отсутствует заголовок запроса Web-клиента.");
            }

            var context = provider.GetOrCreateCurrentSessionContext();
            if (context == null || context.Session == null)
            {
                throw new UnauthorizedAccessException("Выполните вход в Web-клиент.");
            }

            return context;
        }

        /// <summary>
        /// Формирует значение сотрудника для поля Web-карточки.
        /// </summary>
        private object EmployeeValue(StaffEmployee employee)
        {
            if (employee == null)
            {
                return null;
            }

            return new
            {
                id = employee.GetObjectId().ToString(),
                dataType = 64,
                displayName = (employee.LastName + " " + employee.FirstName + " " + employee.MiddleName).Trim(),
                firstName = employee.FirstName,
                lastName = employee.LastName,
                middleName = employee.MiddleName,
                accountName = employee.AccountName,
                unitId = employee.Unit == null ? Guid.Empty.ToString() : employee.Unit.GetObjectId().ToString(),
                departmentName = employee.Unit == null ? "" : employee.Unit.Name,
                isCurrent = false,
                status = 0
            };
        }

        /// <summary>
        /// Формирует значение справочника для поля Web-карточки.
        /// </summary>
        private object DirectoryValue(Guid id, string name, Guid parent)
        {
            return new
            {
                id = id.ToString(),
                name = name,
                description = "",
                parentId = parent.ToString(),
                type = 2,
                dataType = 0,
                hasChildren = false
            };
        }

        /// <summary>
        /// Возвращает начальные значения новой карточки письма.
        /// </summary>
        private JsonResult CreationDefaults(SessionContext context, Guid selectedJournal)
        {
            var c = context.ObjectContext;
            var staff = c.GetService<IStaffService>();
            var employee = staff.GetCurrentEmployee();
            if (employee == null)
            {
                throw new InvalidOperationException("Текущая учетная запись не связана с сотрудником.");
            }

            var unit = employee.Unit;
            if (unit == null)
            {
                throw new InvalidOperationException("У сотрудника не задано подразделение.");
            }

            Guid go = new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e");
            Guid kazan = new Guid("08137765-5066-4e5d-a189-4f459e32feab");
            Guid saratov = new Guid("adc70c7d-54d7-4a13-aa6f-2cc208f572b7");
            Guid journal = selectedJournal;
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

            if (journal != Guid.Empty && journal != go && journal != kazan && journal != saratov)
            {
                throw new InvalidOperationException("Выбран неизвестный журнал исходящих.");
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
            else if (journal != Guid.Empty)
            {
                manager = c.GetObject<StaffUnit>(new Guid("98f2635a-02a4-4af7-92ca-f4bb7674434d")).Manager;
            }

            Guid registrar = journal == go ? new Guid("4adf3c4e-aae8-466e-be68-91eba2c2dc89") : journal == kazan ? new Guid("6dc14225-3c84-49f9-aa26-4f4ea5ce9755") : journal == saratov ? new Guid("bd966761-ac24-4de3-a6c6-23032b8a6b39") : Guid.Empty;
            bool shortAllowed = registrar != Guid.Empty && staff.IsEmployeeInGroup(employee, c.GetObject<StaffGroup>(registrar), true);
            return Json(new { ok = true, defaults = new { author = EmployeeValue(employee), unit = new { id = unit.GetObjectId().ToString(), name = unit.Name, fullName = unit.Name, dataType = 16, departmentType = 1 }, manager = EmployeeValue(manager), journal = journal == Guid.Empty ? null : DirectoryValue(journal, journal == go ? "ГО" : journal == kazan ? "16" : "64", new Guid("6a911880-d1c5-4511-abde-c0b45e4c1f6e")), documentType = DirectoryValue(new Guid("620181fa-7a6a-4be9-883e-55f4060160d5"), "Информационное сообщение", Guid.Empty), delivery = DirectoryValue(new Guid("44a13485-6e98-4775-a394-95f666864830"), "Электронная почта", Guid.Empty), signing = DirectoryValue(new Guid("f0c88d59-f93a-42fd-9da0-ae4d10785470"), "Бумага", Guid.Empty), shortAllowed = shortAllowed } });
        }

        /// <summary>
        /// Импортирует подготовленные разметки исходящей карточки.
        /// </summary>
        [HttpPost]
        public JsonResult PatchCreationLayout()
        {
            try
            {
                var context = Context();
                if (!String.Equals(context.UserInfo.AccountName, Environment.MachineName + "\\Administrator", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Настройка доступна администратору целевого стенда.");
                }

                var importer = SolutionExportServiceFactory.CreateInstance(DocsVision.WebClientLibrary.ObjectModel.LayoutFormat.FolderWithReadableXmlFiles);
                var solutions = context.ObjectContext.GetService<DocsVision.WebClientLibrary.ObjectModel.Services.ISolutionsService>();
                var solution = solutions.GetSolutions().First(s => s.SolutionId == new Guid("a7de89d0-26d5-426d-834f-44395016d109"));
                int before = solution.Layouts.Count;
                string backup = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/CreationLayoutBackup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                importer.ExportSolution(context, solution, backup);
                importer.ImportSolution(context, @"C:\AGORA-OUT\web-runtime\CreationLayoutFix\Solution.xml", true);
                if (importer.Errors != null && importer.Errors.Count > 0)
                {
                    throw new InvalidOperationException(String.Join("; ", importer.Errors));
                }

                return Json(new { ok = true, layout = "dee24b13-ce6d-4d1a-8446-d36a902bdbcb", backup = backup, layoutsBefore = before, layoutsAfter = solution.Layouts.Count });
            }
            catch (Exception e)
            {
                Response.StatusCode = 400;
                Response.TrySkipIisCustomErrors = true;
                return Json(new { ok = false, message = e.ToString() });
            }
        }

        /// <summary>
        /// Выполняет запрошенную операцию исходящей карточки.
        /// </summary>
        [HttpPost]
        public JsonResult Execute(WebAction request)
        {
            try
            {
                if (request == null)
                {
                    throw new ArgumentException("Пустой запрос.");
                }

                var context = Context();
                string name = request.Method;
                if (name == "GetOutgoingCreationDefaults")
                {
                    return CreationDefaults(context, request.Journal);
                }

                var allowed = new[]
                {
                    "ValidateOutgoingJournalChange",
                    "CancelOutgoingDocument",
                    "GetOutgoingWebContext",
                    "AssignDraftNumber",
                    "GenerateMainLetter",
                    "StartOutgoingApproval",
                    "BeginShortRegistration",
                    "GetOutgoingTaskActions",
                    "CompleteOutgoingTask",
                    "MarkOutgoingSent",
                    "ConfirmOutgoingSignature",
                    "GetOutgoingDocumentOperations",
                    "GetOutgoingApprovalHistory",
                    "CreateOutgoingFamiliarizationDraft",
                    "SendOutgoingDocumentMessage",
                    "RequestOutgoingAccessRefresh"
                };
                if (!allowed.Contains(name))
                {
                    throw new UnauthorizedAccessException("Неподдерживаемая операция.");
                }

                var method = context.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation", name);
                if (name == "CompleteOutgoingTask")
                {
                    method.Parameters.AddNew("taskId", ParameterValueType.Guid, request.TaskId);
                    method.Parameters.AddNew("semantic", ParameterValueType.Integer, request.Semantic);
                    method.Parameters.AddNew("comment", ParameterValueType.String, request.Comment ?? "");
                }
                else
                {
                    method.Parameters.AddNew("cardId", ParameterValueType.Guid, request.CardId);
                    if (name == "ValidateOutgoingJournalChange")
                    {
                        method.Parameters.AddNew("journal", ParameterValueType.Guid, request.Journal);
                    }

                    if (name == "GetOutgoingWebContext")
                    {
                        method.Parameters.AddNew("initialize", ParameterValueType.Bool, request.Initialize);
                    }

                    if (name == "SendOutgoingDocumentMessage")
                    {
                        method.Parameters.AddNew("recipientEmail", ParameterValueType.String, request.RecipientEmail ?? "");
                        method.Parameters.AddNew("message", ParameterValueType.String, request.Message ?? "");
                    }
                }

                string result = Convert.ToString(method.Execute());
                if (request.CardId != Guid.Empty)
                {
                    try
                    {
                        context.Session.CardManager.GetCardData(request.CardId).Refresh();
                    }
                    catch
                    {
                    }
                }

                return Json(new { ok = true, xml = result });
            }
            catch (Exception e)
            {
                Response.StatusCode = 400;
                Response.TrySkipIisCustomErrors = true;
                return Json(new { ok = false, message = e.Message });
            }
        }

        /// <summary>
        /// Возвращает сведения для инициализации интерфейса карточки.
        /// </summary>
        [HttpPost]
        public JsonResult Bootstrap(bool standardOnly = false)
        {
            try
            {
                Server.ScriptTimeout = 1800;
                var context = Context();
                string expected = Environment.MachineName + "\\Administrator";
                if (!String.Equals(context.UserInfo.AccountName, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Настройка доступна администратору целевого стенда.");
                }

                var importer = SolutionExportServiceFactory.CreateInstance(DocsVision.WebClientLibrary.ObjectModel.LayoutFormat.FolderWithReadableXmlFiles);
                string root = @"C:\Program Files (x86)\Docsvision\WebClient\5.5\LayoutSolutions";
                string marker = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/agora-standard-solutions-imported.txt");
                var imported = new List<string>();
                if (!System.IO.File.Exists(marker))
                {
                    foreach (string name in new[]
                    {
                        "BasicSolution",
                        "DocumentManagementSolution"
                    }

                    )
                    {
                        System.IO.File.WriteAllText(System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/agora-import-progress.txt"), "Importing " + name + " " + DateTime.UtcNow.ToString("o"));
                        importer.ImportSolution(context, Path.Combine(root, name, "Solution.xml"), true);
                        if (importer.Errors != null && importer.Errors.Count > 0)
                        {
                            throw new InvalidOperationException(String.Join("; ", importer.Errors));
                        }

                        imported.Add(name);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(marker));
                    System.IO.File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                }

                var ss = context.ObjectContext.GetService<DocsVision.BackOffice.ObjectModel.Services.IStateService>();
                var rs = context.ObjectContext.GetService<DocsVision.BackOffice.ObjectModel.Services.IRoleModelService>();
                var kind = context.ObjectContext.GetObject<DocsVision.BackOffice.ObjectModel.KindsCardKind>(new Guid("7cf57200-ae68-4bca-ba7f-2243a9f783bc"));
                var settings = ss.GetKindStateSetting(kind);
                var roles = rs.GetKindRoleSettings(kind);
                var partial = settings.Operations.FirstOrDefault(o => o.DefaultName == "AGORA.EditPartialFields");
                if (partial == null)
                {
                    partial = ss.AddOperation(settings, "AGORA.EditPartialFields", false);
                    ss.AddOperationLocalizedName(partial, 1049, "Редактирование подписанта и содержания");
                    context.ObjectContext.SaveObject(partial);
                }

                foreach (var state in settings.States)
                {
                    if ((state.GetObjectId() == new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6") || state.GetObjectId() == new Guid("bcc23707-c0aa-4b2c-bbec-3b6ff751aca5") || state.GetObjectId() == new Guid("b7be72ff-4ede-490d-85db-22ee324e27f4")) && !settings.StateMachineBranches.Any(b => b.StartState == state && b.EndState == state && b.Operation == partial))
                    {
                        var branch = ss.AddBranch(settings, state, state, partial);
                        context.ObjectContext.SaveObject(branch);
                    }

                    foreach (var role in roles.Roles)
                    {
                        bool allow = (role.GetObjectId() == new Guid("e207f91f-5f28-468d-924a-e948d45731f8") && (state.GetObjectId() == new Guid("ab48f98d-3637-48aa-92bd-556bc88475c6") || state.GetObjectId() == new Guid("bcc23707-c0aa-4b2c-bbec-3b6ff751aca5"))) || (role.Name.StartsWith("АГОРА — Матрица 4 —", StringComparison.Ordinal) && state.GetObjectId() == new Guid("b7be72ff-4ede-490d-85db-22ee324e27f4"));
                        if (allow && !roles.Links.Any(l => l.Role == role && l.State == state && l.Operation == partial))
                        {
                            rs.AddLink(roles, partial, role, state, DocsVision.BackOffice.ObjectModel.RoleModelOperationStatus.Allowed);
                        }
                    }
                }

                context.ObjectContext.SaveObject(rs.RolesDictionary);
                context.ObjectContext.SaveObject(ss.StatesDictionary);
                context.ObjectContext.AcceptChanges();
                var operations = settings.Operations.ToDictionary(o => o.DefaultName, o => o.GetObjectId().ToString());
                if (standardOnly)
                {
                    return Json(new { ok = true, imported = imported, operations = operations });
                }

                importer.ImportSolution(context, @"C:\AGORA-OUT\web-runtime\AgoraSolution\Solution.xml", true);
                if (importer.Errors != null && importer.Errors.Count > 0)
                {
                    throw new InvalidOperationException(String.Join("; ", importer.Errors));
                }

                imported.Add("AgoraSolution");
                System.IO.File.WriteAllText(System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/agora-import-progress.txt"), "Imported AgoraSolution " + DateTime.UtcNow.ToString("o"));
                return Json(new { ok = true, imported = imported });
            }
            catch (Exception e)
            {
                Response.StatusCode = 400;
                Response.TrySkipIisCustomErrors = true;
                return Json(new { ok = false, message = e.ToString() });
            }
        }
    }
}
