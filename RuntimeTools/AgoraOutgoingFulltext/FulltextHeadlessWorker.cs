using System;
using System.IO;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;
using DocsVision.Platform.FullTextService.ObjectModel;

public sealed class AgoraFulltextScriptWorker : IScriptWorker
{
    /// <summary>
    /// Выполняет полнотекстовую обработку без интерактивного интерфейса.
    /// </summary>
    public bool ExecuteScript(string databaseAlias, string connectionString, string script, string caption)
    {
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (!connection.IntegratedSecurity || !String.Equals(connection.InitialCatalog, "DV002_Exam", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected indexing target");
        }

        string root = @"C:\AGORA-OUT\fulltext-runtime";
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "native-index-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".sql");
        File.WriteAllText(file, script, new UTF8Encoding(true));
        var start = new ProcessStartInfo("sqlcmd.exe", "-E -S localhost -d DV002_Exam -b -i \"" + file + "\"");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string log = output.Result + Environment.NewLine + error.Result;
            File.WriteAllText(file + ".log", log, Encoding.UTF8);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Native fulltext SQL script failed: " + log);
            }

            return true;
        }
    }
}
