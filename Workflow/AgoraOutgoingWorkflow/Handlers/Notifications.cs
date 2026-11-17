using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using DocsVision.Platform.ObjectManager;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.ApprovalDesigner.ObjectModel;
using DocsVision.ApprovalDesigner.ObjectModel.Services;

namespace Agora.Outgoing.Workflow
{
    public static partial class Runtime
    {
        public sealed class MailConfig
        {
            public string mode;
            public string sender;
            public string cardUrlTemplate;
            public string smtpHost;
            public string mailbox;
            public string credentialFile;
            public int smtpPort;
            public bool enableSsl;
            public bool useDefaultCredentials;
            public bool testServer;
            public bool externalDelivery;
        }

        /// <summary>
        /// Уведомляет участников о зарегистрированном подписанном письме.
        /// </summary>
        private static void Notify()
        {
            var cfg = Read<MailConfig>(Path.Combine(Root, @"notification-runtime\notifications.json"));
            if (cfg.mode != "smtp" && cfg.mode != "pickup")
            {
                throw new InvalidOperationException("Unsupported mail mode");
            }

            if (!String.IsNullOrWhiteSpace(cfg.credentialFile))
            {
                throw new InvalidOperationException("Legacy PowerShell credential file is unsupported; configure native SMTP credentials");
            }

            string state = Path.Combine(StateRoot, "notifications");
            string pickup = Path.Combine(state, "pickup");
            Directory.CreateDirectory(pickup);
            var taskService = Context.GetService<ITaskService>();
            var stageService = Context.GetService<IApprovalTaskService>();
            int failures = 0;
            foreach (CardData raw in Session.CardManager.FindCards(Query(null).GetXml()))
            {
                try
                {
                    NotifyDocument(raw, cfg, taskService, stageService, state, pickup);
                }
                catch (Exception ex)
                {
                    failures++;
                    Log("notifications", new { card = raw.Id, error = ex.ToString() });
                }
            }

            if (failures > 0)
            {
                throw new InvalidOperationException("Notification failed cards: " + failures);
            }
        }

        /// <summary>
        /// Проверяет подписанный документ и уведомляет его участников.
        /// </summary>
        private static void NotifyDocument(CardData raw, MailConfig cfg, ITaskService taskService, IApprovalTaskService stageService, string state, string pickup)
        {
            var stateId = Id(raw.Sections[SystemSection].FirstRow, "State");
            if (!new[]
            {
                new Guid("11b0eb5d-3fa1-48a9-ba0b-e9ed1c2b1510"),
                new Guid("75fe7ede-fbaa-4c32-b3eb-16e1e7b99b9f"),
                new Guid("cc5c57be-c7de-4d72-84cb-244ab0efce39")
            }.Contains(stateId))
            {
                return;
            }

            var extra = raw.Sections[ExtraSection].FirstRow;
            Guid signature = Id(extra, "VerifiedSignatureId");
            Guid version = Id(extra, "SignedFileVersion");
            Guid pdf = Id(extra, "RegistrationPdfRow");
            if (signature == Guid.Empty || version == Guid.Empty || pdf == Guid.Empty)
            {
                return;
            }

            var doc = Context.GetObject<Document>(raw.Id);
            var main = doc.Files.Where(f => f.FileType == DocumentFileType.Main).ToArray();
            if (main.Length != 1 || main[0].FileVersionId != version || !doc.Files.Any(f => f.GetObjectId() == pdf))
            {
                throw new InvalidOperationException("Registered file does not match verified signature provenance");
            }

            if (doc.MainInfo.SignatureList == null || !doc.MainInfo.SignatureList.Signatures.Any(s => s.GetObjectId() == signature))
            {
                throw new InvalidOperationException("Verified native signature missing");
            }

            var recipients = new Dictionary<Guid, StaffEmployee>();
            if (doc.MainInfo.Author != null)
            {
                recipients[doc.MainInfo.Author.GetObjectId()] = doc.MainInfo.Author;
            }

            Guid history = Id(extra, "ActiveApprovalId");
            if (history != Guid.Empty)
            {
                var reconcile = Context.GetObject<Reconcile>(history);
                foreach (var entry in reconcile.MainInfo.TaskList.Tasks)
                {
                    var task = entry.Task;
                    if (task == null || !taskService.IsTaskCompleted(task) || task.MainInfo.CompletedUser == null)
                    {
                        continue;
                    }

                    var stage = stageService.GetTaskApprovalStage(task);
                    if (stage.MainInfo.Mode != ApprovalStageMode.Approval || stage.MainInfo.ReconcileType != ApprovalType.Alternative)
                    {
                        continue;
                    }

                    var decision = stage.Decisions.FirstOrDefault(d => task.MainInfo.CompletionResult != null && d.Name == task.MainInfo.CompletionResult.Name);
                    if (decision != null && decision.Semantics == DecisionSemantics.Positive)
                    {
                        recipients[task.MainInfo.CompletedUser.GetObjectId()] = task.MainInfo.CompletedUser;
                    }
                }
            }

            foreach (var recipient in recipients.Values)
            {
                SendNotification(raw, doc, signature, recipient, cfg, state, pickup);
            }
        }

        /// <summary>
        /// Отправляет уведомление сотруднику без повторной доставки.
        /// </summary>
        private static void SendNotification(CardData raw, Document doc, Guid signature, StaffEmployee recipient, MailConfig cfg, string state, string pickup)
        {
            string key = raw.Id.ToString("N") + "-" + signature.ToString("N") + "-" + recipient.GetObjectId().ToString("N");
            string marker = Path.Combine(state, key + ".json");
            if (File.Exists(marker))
            {
                return;
            }

            if (String.IsNullOrWhiteSpace(recipient.Email))
            {
                throw new InvalidOperationException("Recipient has no email");
            }

            using (var mail = new MailMessage())
            using (var smtp = new SmtpClient())
            {
                mail.From = new MailAddress(cfg.sender);
                mail.To.Add(recipient.Email);
                mail.Subject = "Исходящее письмо подписано в СЭД";
                mail.SubjectEncoding = mail.BodyEncoding = Encoding.UTF8;
                mail.IsBodyHtml = true;
                string url = cfg.cardUrlTemplate.Replace("{cardId}", raw.Id.ToString());
                mail.Body = "Уважаемый коллега! Письмо <a href=\"" + WebUtility.HtmlEncode(url) + "\">" + WebUtility.HtmlEncode(doc.MainInfo.Name) + "</a> подписано в СЭД! При необходимости, распечатайте и отнесите на подпись!";
                mail.Headers.Add("X-AGORA-Notification-Key", key);
                mail.Headers.Add("Message-ID", "<" + key + "@agora.test>");
                if (cfg.mode == "smtp")
                {
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.Host = cfg.smtpHost;
                    smtp.Port = cfg.smtpPort;
                    smtp.EnableSsl = cfg.enableSsl;
                    smtp.UseDefaultCredentials = cfg.useDefaultCredentials;
                    smtp.Timeout = 30000;
                }
                else
                {
                    smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
                    smtp.PickupDirectoryLocation = pickup;
                }

                bool exists = cfg.mode == "pickup" ? Directory.GetFiles(pickup, "*.eml").Any(f => File.ReadAllText(f).Contains("X-AGORA-Notification-Key: " + key)) : (cfg.testServer && !String.IsNullOrEmpty(cfg.mailbox) && File.Exists(Path.Combine(cfg.mailbox, key + ".eml")));
                if (!exists)
                {
                    smtp.Send(mail);
                }

                Save(marker, new { card = raw.Id.ToString(), signature = signature.ToString(), recipient = recipient.GetObjectId().ToString(), status = cfg.mode == "smtp" ? "accepted_by_smtp" : "queued_local_pickup", deliveryMode = cfg.mode, externalDelivery = cfg.externalDelivery, utc = DateTime.UtcNow.ToString("o") });
            }
        }
    }
}
