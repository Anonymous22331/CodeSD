using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using Agora.Outgoing.Deployment;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using Microsoft.CSharp;

namespace Agora.Outgoing.Workflow
{
    /// <summary>
    /// Обновляет исходный код клиентского сценария в справочнике скриптов Docsvision.
    /// </summary>
    public static class ClientScriptMigration
    {
        private const string SourcePath = @"C:\AGORA-OUT\csharp-runtime\AgoraOutgoingScript.cs";

        /// <summary>
        /// Проверяет компиляцию и сохраняет оформленный сценарий исходящей карточки.
        /// </summary>
        public static void Apply()
        {
            Bootstrap.Initialize();
            CompileSource();
            var context = ContextConnection.Open("dv002_exam");
            try
            {
                var kind = context.GetObject<KindsCardKind>(new Guid("7cf57200-ae68-4bca-ba7f-2243a9f783bc"));
                var service = context.GetService<IScriptingService>();
                var script = context.GetObject<ScriptingScript>(new Guid("4978a008-2802-4d97-8453-35021b319317"));
                dynamic code = script.Code;
                var backup = Path.Combine(Runtime.Root, "backups", "client-script-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cs");
                File.WriteAllText(backup, (string)code.Code);
                code.SetSession(ContextConnection.Session);
                code.SetContext(context);
                code.Code = File.ReadAllText(SourcePath);
                kind.Script = script;
                context.SaveObject(service.Scripts);
                context.SaveObject(kind);
                context.AcceptChanges();
                Runtime.Save(Path.Combine(Runtime.Root, @"csharp-runtime\client-script-migration.json"), new { status = "saved", backup = backup, scriptId = script.GetObjectId().ToString() });
            }
            finally
            {
                ContextConnection.Close();
            }
        }

        /// <summary>
        /// Компилирует сценарий с установленными клиентскими сборками Docsvision 5.5.
        /// </summary>
        private static void CompileSource()
        {
            const string client = @"C:\Program Files (x86)\Docsvision\5.5\Client";
            var parameters = new CompilerParameters();
            parameters.OutputAssembly = Path.Combine(Runtime.Root, @"csharp-runtime\Agora.Outgoing.ClientRules.dll");
            foreach (var reference in new[]
            {
                "System.dll",
                "System.Core.dll",
                "System.Xml.dll",
                "System.Windows.Forms.dll",
                "System.Drawing.dll"
            }

            )
            {
                parameters.ReferencedAssemblies.Add(reference);
            }

            foreach (var reference in new[]
            {
                "DocsVision.Platform.dll",
                "DocsVision.Platform.Extensibility.dll",
                "DocsVision.Platform.CardHost.dll",
                "DocsVision.Platform.WinForms.dll",
                "DocsVision.Platform.ObjectManager.dll",
                "DocsVision.Platform.ObjectModel.dll",
                @"BaseObjects\DocsVision.BackOffice.ObjectModel.dll",
                @"BaseObjects\DocsVision.BackOffice.WinForms.dll"
            }

            )
            {
                parameters.ReferencedAssemblies.Add(Path.Combine(client, reference));
            }

            using (var compiler = new CSharpCodeProvider())
            {
                var result = compiler.CompileAssemblyFromFile(parameters, SourcePath);
                if (result.Errors.HasErrors)
                {
                    throw new InvalidOperationException(String.Join(Environment.NewLine, result.Errors.Cast<CompilerError>().Select(error => error.ToString())));
                }
            }
        }
    }
}
