using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Text;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Web.Script.Serialization;

internal static class HostCrlBridge
{
    sealed class Config
    {
        public string vmrun;
        public string vm;
    }

    /// <summary>
    /// Экранирует аргумент командной строки Windows.
    /// </summary>
    private static string Quote(string value)
    {
        return "\"" + System.Text.RegularExpressions.Regex.Replace(value, @"(\\*)\""", "$1$1\\\"") + new string ('\\', value.Reverse().TakeWhile(c => c == '\\').Count()) + "\"";
    }

    /// <summary>
    /// Выполняет операцию VMware и проверяет код завершения.
    /// </summary>
    private static void Invoke(Config cfg, string user, string password, params string[] operation)
    {
        var args = new[]
        {
            "-T",
            "player",
            "-gu",
            user,
            "-gp",
            password
        }.Concat(operation);
        var start = new ProcessStartInfo(cfg.vmrun, String.Join(" ", args.Select(Quote)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                throw new IOException("VMware operation timed out");
            }

            if (process.ExitCode != 0)
            {
                throw new IOException("VMware operation failed: " + operation[0] + "; exit " + process.ExitCode);
            }
        }
    }

    /// <summary>
    /// Передаёт CRL в ВМ и запускает проверку и импорт через C#.
    /// </summary>
    private static int Main()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"AGORA-OUT\crypto-bridge");
        var json = new JavaScriptSerializer();
        try
        {
            var cfg = json.Deserialize<Config>(File.ReadAllText(Path.Combine(dir, "bridge-config.json")));
            var xml = new XmlDocument
            {
                XmlResolver = null
            };
            xml.Load(Path.Combine(dir, "guest-credential.xml"));
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("p", "http://schemas.microsoft.com/powershell/2004/04");
            string user = xml.SelectSingleNode("//p:S[@N='UserName']", ns).InnerText;
            string hex = xml.SelectSingleNode("//p:SS[@N='Password']", ns).InnerText;
            byte[] protectedBytes = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            string password;
            try
            {
                password = Encoding.Unicode.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }

            var physical = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var search = new ManagementObjectSearcher("SELECT GUID FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True"))
                foreach (ManagementObject item in search.Get())
                {
                    if (item["GUID"] != null)
                    {
                        physical.Add((string)item["GUID"]);
                    }
                }

            var adapter = NetworkInterface.GetAllNetworkInterfaces().First(n => physical.Contains(n.Id) && n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
            int index = adapter.GetIPProperties().GetIPv4Properties().Index;
            foreach (var spec in new[]
            {
                new[]
                {
                    "fns-2025-current.crl",
                    "http://uc.nalog.ru/cdp/aeec619ebfcd5254b7ebb5c5b2842e161bb21f4c.crl"
                },
                new[]
                {
                    "guc2022-current.crl",
                    "http://crl.gosuslugi.ru/cdp/guc2022.crl"
                }
            }

            )
            {
                string path = Path.Combine(dir, spec[0]);
                AgoraDirectCrlDownload.Download(spec[1], index, path + ".download");
                File.Copy(path + ".download", path, true);
                File.Delete(path + ".download");
                Invoke(cfg, user, password, "copyFileFromHostToGuest", cfg.vm, path, @"C:\AGORA-OUT\crypto-runtime\relay-inbox\" + spec[0]);
            }

            Invoke(cfg, user, password, "runProgramInGuest", cfg.vm, @"C:\AGORA-OUT\csharp-runtime\Agora.Outgoing.Runner.exe", "crl");
            File.WriteAllText(Path.Combine(dir, "last-result.json"), json.Serialize(new { utc = DateTime.UtcNow.ToString("o"), status = "updated_and_verified", implementation = "C#", physicalInterface = adapter.Name }));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "last-result.json"), json.Serialize(new { utc = DateTime.UtcNow.ToString("o"), status = "error", implementation = "C#", error = ex.ToString() }));
            return 1;
        }
    }
}
