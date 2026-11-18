using System;
using System.IO;
using Agora.Outgoing.Workflow;

internal static class Program
{
    /// <summary>
    /// Запускает выбранный режим фонового обработчика АГОРА.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            PlatformAssemblyResolver.Initialize();
            if (args.Length != 1)
            {
                throw new ArgumentException("Specify delivery, archive, access, registration, notifications, crl or smtp");
            }

            string mode = args[0];
            if (mode == "smtp")
            {
                AgoraTestSmtp.Run(@"C:\ProgramData\Docsvision\AGORA-OUT\test-mailbox", 2525);
            }
            else
            {
                Runtime.Run(mode);
            }

            return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(@"C:\AGORA-OUT\csharp-runtime\runner-errors.log", DateTime.UtcNow.ToString("o") + " " + ex + Environment.NewLine);
            return 1;
        }
    }

}
