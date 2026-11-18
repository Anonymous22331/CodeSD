using System;
using System.IO;
using System.Net.Mail;
using System.Xml;
using Agora.Outgoing.Workflow;

internal static class SmokeCheck
{
    /// <summary>
    /// Проверяет отказ для неподтверждённой подписи и тестовую почту.
    /// </summary>
    [STAThread]
    private static int Main()
    {
        try
        {
            PlatformAssemblyResolver.Initialize();
            bool rejected = false;
            var xml = new XmlDocument();
            xml.LoadXml("<SignatureConfirmationResult status='error'/>");
            try
            {
                RegisteredPdf.Create("missing.docx", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf"), "TEST", DateTime.Today, xml.DocumentElement);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            if (!rejected)
            {
                throw new Exception("Unverified signature accepted");
            }

            using (var mail = new MailMessage("docsvision@agora.test", "docsvision@agora.test", "AGORA C# migration check", "Проверка тестового SMTP после удаления зависимости от PowerShell."))
            using (var smtp = new SmtpClient("127.0.0.1", 2525))
            {
                smtp.Timeout = 10000;
                smtp.Send(mail);
            }

            File.WriteAllText(@"C:\AGORA-OUT\csharp-runtime\smoke-check.json", "{\"unverifiedSignatureRejected\":true,\"smtpAccepted\":true,\"implementation\":\"C#\"}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\AGORA-OUT\csharp-runtime\smoke-check.json", ex.ToString());
            return 1;
        }
    }
}
