using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocsVision.Platform.WebClient;
using DocsVision.WebClientLibrary.ObjectModel.SolutionsDirectory.Export;

namespace Agora.Outgoing.Web
{
    /// <summary>Отделяет административную настройку решения от пользовательских операций.</summary>
    public sealed class OutgoingSetupService : IOutgoingSetupService
    {
        private readonly DocsVision.Platform.WebClient.Web.IEnvironmentService environment;

        /// <summary>Получает сервис окружения для путей и проверки администратора.</summary>
        public OutgoingSetupService(DocsVision.Platform.WebClient.Web.IEnvironmentService environment)
        {
            this.environment = environment;
        }

        /// <summary>Проверяет право администратора на настройку решения.</summary>
        private void EnsureAdministrator(SessionContext context)
        {
            string expected = environment.MachineName + "\\Administrator";
            if (!String.Equals(context.UserInfo.AccountName, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Настройка доступна администратору целевого стенда.");
            }
        }

        /// <summary>Возвращает каталог ресурсов установки из конфигурации.</summary>
        private string GetRuntimeDirectory()
        {
            string configured = System.Configuration.ConfigurationManager.AppSettings["AgoraOutgoing.RuntimeDirectory"];
            return String.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Docsvision", "AgoraOutgoing")
            : Path.GetFullPath(configured);
        }

        /// <summary>Импортирует разметку после создания резервной копии.</summary>
        public object PatchCreationLayout(SessionContext context)
        {
            EnsureAdministrator(context);

            var importer = SolutionExportServiceFactory.CreateInstance(DocsVision.WebClientLibrary.ObjectModel.LayoutFormat.FolderWithReadableXmlFiles);
            var solutions = context.ObjectContext.GetService<DocsVision.WebClientLibrary.ObjectModel.Services.ISolutionsService>();
            var solution = solutions.GetSolutions().First(s => s.SolutionId == new Guid("a7de89d0-26d5-426d-834f-44395016d109"));
            int before = solution.Layouts.Count;
            string backup = environment.MapPath("~/App_Data/CreationLayoutBackup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            importer.ExportSolution(context, solution, backup);
            importer.ImportSolution(context, Path.Combine(GetRuntimeDirectory(), "CreationLayoutFix", "Solution.xml"), true);
            if (importer.Errors != null && importer.Errors.Count > 0)
            {
                throw new InvalidOperationException(String.Join("; ", importer.Errors));
            }

            return new
            {
                ok = true,
                layout = "dee24b13-ce6d-4d1a-8446-d36a902bdbcb",
                backup = backup,
                layoutsBefore = before,
                layoutsAfter = solution.Layouts.Count
            };
        }

        /// <summary>Настраивает решения, операции и ролевую модель.</summary>
        public object Bootstrap(SessionContext context, bool standardOnly)
        {
            EnsureAdministrator(context);

            var importer = SolutionExportServiceFactory.CreateInstance(DocsVision.WebClientLibrary.ObjectModel.LayoutFormat.FolderWithReadableXmlFiles);
            var imported = ImportStandardSolutions(context);
            var operations = ConfigurePartialFieldPermissions(context);
            if (standardOnly)
            {
                return new
                {
                    ok = true,
                    imported = imported,
                    operations = operations
                };
            }

            importer.ImportSolution(context, Path.Combine(GetRuntimeDirectory(), "AgoraSolution", "Solution.xml"), true);
            if (importer.Errors != null && importer.Errors.Count > 0)
            {
                throw new InvalidOperationException(String.Join("; ", importer.Errors));
            }

            imported.Add("AgoraSolution");
            System.IO.File.WriteAllText(environment.MapPath("~/App_Data/agora-import-progress.txt"), "Imported AgoraSolution " + DateTime.UtcNow.ToString("o"));
            return new
            {
                ok = true,
                imported = imported
            };
        }

        /// <summary>Импортирует стандартные решения один раз по маркеру установки.</summary>
        private List<string> ImportStandardSolutions(SessionContext context)
        {
            var importer = SolutionExportServiceFactory.CreateInstance(DocsVision.WebClientLibrary.ObjectModel.LayoutFormat.FolderWithReadableXmlFiles);
            string root = Path.GetFullPath(Path.Combine(environment.SiteRootDir, "..", "LayoutSolutions"));
            string marker = environment.MapPath("~/App_Data/agora-standard-solutions-imported.txt");
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
                    System.IO.File.WriteAllText(environment.MapPath("~/App_Data/agora-import-progress.txt"), "Importing " + name + " " + DateTime.UtcNow.ToString("o"));
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

            return imported;
        }

        /// <summary>Добавляет разрешенное редактирование полей по состояниям и ролям.</summary>
        private Dictionary<string, string> ConfigurePartialFieldPermissions(SessionContext context)
        {
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
            return operations;
        }


    }
}
