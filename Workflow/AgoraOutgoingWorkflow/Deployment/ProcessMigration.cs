using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Agora.Outgoing.Deployment;
using DocsVision.Workflow.Functions;
using DocsVision.Workflow.Objects;

namespace Agora.Outgoing.Workflow
{
    /// <summary>
    /// Подключает сборку обработчиков к штатным программным узлам Docsvision.
    /// </summary>
    public static class ProcessMigration
    {
        private const string RuntimeDirectory = @"C:\AGORA-OUT\csharp-runtime";
        private const string ManifestPath = @"C:\AGORA-OUT\workflow-runtime\processes.json";

        /// <summary>
        /// Возвращает элементы коллекции объектов Workflow.
        /// </summary>
        private static IEnumerable<dynamic> Values(object collection)
        {
            var property = collection.GetType().GetProperty("Values");
            var values = (IEnumerable)property.GetValue(collection, null);
            return values.Cast<object>().Select(value => (dynamic)value);
        }

        /// <summary>
        /// Обновляет программные узлы или сохраняет их текущее состояние.
        /// </summary>
        public static void Apply(bool auditOnly)
        {
            Bootstrap.Initialize();
            ContextConnection.Open("dv002_exam");
            Library library = null;
            try
            {
                library = new Library(ContextConnection.Session);
                var manifest = Runtime.Read<Dictionary<string, object>>(ManifestPath);
                var reports = new List<object>();
                var backup = Path.Combine(Runtime.Root, "backups", "workflow-reference-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (!auditOnly)
                {
                    Directory.CreateDirectory(backup);
                }

                foreach (var mode in new[]
                {
                    "Delivery",
                    "Archive"
                }

                )
                {
                    var entry = (Dictionary<string, object>)manifest[mode];
                    foreach (var role in new[]
                    {
                        "template",
                        "instance"
                    }

                    )
                    {
                        var process = library.GetProcess(new Guid((string)entry[role]));
                        var body = GetScriptFunction(process);
                        if (!auditOnly)
                        {
                            var settings = CreateSettings(File.ReadAllText(Path.Combine(RuntimeDirectory, mode + "Process.cs")));
                            UpdateScript(process, body, settings, library, role, backup, mode);
                        }

                        reports.Add(CreateReport(process, body, mode, role));
                    }
                }

                Runtime.Save(Path.Combine(RuntimeDirectory, auditOnly ? "process-audit.json" : "process-migration.json"), new { utc = DateTime.UtcNow.ToString("o"), processes = reports, backup = auditOnly ? null : backup });
            }
            finally
            {
                if (library != null)
                {
                    library.Shutdown();
                }

                ContextConnection.Close();
            }
        }

        /// <summary>
        /// Находит единственный программный узел процесса.
        /// </summary>
        private static dynamic GetScriptFunction(Process process)
        {
            var scripts = Values(process.Functions).Where(function => ((string)function.Data ?? String.Empty).Contains("CodeSource")).ToArray();
            if (scripts.Length != 1)
            {
                throw new InvalidOperationException("Ожидается один программный узел: " + process.ID);
            }

            return scripts[0];
        }

        /// <summary>
        /// Создаёт настройки узла со штатной ссылкой на сборку обработчиков.
        /// </summary>
        private static string CreateSettings(string source)
        {
            var document = new XmlDocument();
            var settings = document.CreateElement("Settings");
            document.AppendChild(settings);
            var language = document.CreateElement("CSharp");
            language.InnerText = "True";
            settings.AppendChild(language);
            var code = document.CreateElement("CodeSource");
            code.InnerText = source;
            settings.AppendChild(code);
            var references = document.CreateElement("Files");
            var reference = document.CreateElement("File");
            reference.InnerText = Path.Combine(RuntimeDirectory, "Agora.Outgoing.Workflow.dll");
            references.AppendChild(reference);
            settings.AppendChild(references);
            return document.OuterXml;
        }

        /// <summary>
        /// Проверяет компиляцию и обновляет узел с сохранением данных для отката.
        /// </summary>
        private static void UpdateScript(Process process, dynamic body, string settings, Library library, string role, string backup, string mode)
        {
            var checker = new ScriptFunction();
            checker.FunctionData = body;
            checker.Data = settings;
            checker.CheckCompile();
            var originalSettings = (string)body.Data;
            var originalStates = new Dictionary<Guid, string>();
            foreach (dynamic state in body.States)
            {
                originalStates[(Guid)state.ID] = (string)state.Data;
            }

            File.WriteAllText(Path.Combine(backup, mode + "-" + role + "-script.xml"), originalSettings);
            Runtime.Save(Path.Combine(backup, mode + "-" + role + "-states.json"), originalStates.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
            var resume = role == "instance" && process.State == ProcessStateEnum.Active;
            var actor = Environment.MachineName + "\\Administrator";
            if (resume)
            {
                process.Pause(actor);
            }

            try
            {
                WriteSettings(process, body, settings, null);
                if (role == "template")
                {
                    process.Prepare();
                    process.BeginUpdate();
                    try
                    {
                        process.TemplateState = TemplateStateEnum.InUse;
                        process.EndUpdate();
                    }
                    catch
                    {
                        process.CancelUpdate();
                        throw;
                    }
                }
            }
            catch
            {
                WriteSettings(process, body, originalSettings, originalStates);
                throw;
            }
            finally
            {
                if (resume)
                {
                    process.Resume(actor, library.Dictionary);
                }
            }
        }

        /// <summary>
        /// Сохраняет настройки функции и её проходов через API Workflow.
        /// </summary>
        private static void WriteSettings(Process process, dynamic body, string settings, Dictionary<Guid, string> originalStates)
        {
            process.BeginUpdate();
            try
            {
                body.Data = settings;
                foreach (dynamic state in body.States)
                {
                    state.Data = originalStates == null ? settings : originalStates[(Guid)state.ID];
                }

                process.EndUpdate();
            }
            catch
            {
                process.CancelUpdate();
                throw;
            }
        }

        /// <summary>
        /// Формирует отчёт о коде, ссылках на сборки и состоянии процесса.
        /// </summary>
        private static object CreateReport(Process process, dynamic body, string mode, string role)
        {
            var execution = new List<object>();
            foreach (dynamic function in Values(process.Functions))
            {
                var passes = new List<object>();
                foreach (dynamic pass in function.States)
                {
                    passes.Add(new { state = pass.State.ToString(), hasErrors = (bool)pass.HasErrors, executeTime = ((DateTime)pass.ExecuteTime).ToString("o") });
                }

                execution.Add(new { caption = (string)function.Caption, states = passes });
            }

            var document = new XmlDocument
            {
                XmlResolver = null
            };
            document.LoadXml((string)body.Data);
            var source = document.SelectSingleNode("/Settings/CodeSource").InnerText;
            var references = document.SelectNodes("/Settings/Files/File").Cast<XmlNode>().Select(node => node.InnerText).ToArray();
            return new
            {
                mode = mode,
                role = role,
                id = process.ID.ToString(),
                state = process.State.ToString(),
                containsPowerShell = source.IndexOf("powershell", StringComparison.OrdinalIgnoreCase) >= 0 || source.Contains(".ps1"),
                usesReflection = source.Contains("Assembly.Load") || source.Contains("GetMethod(") || source.Contains(".Invoke("),
                references = references,
                execution = execution,
                code = source
            };
        }
    }
}
