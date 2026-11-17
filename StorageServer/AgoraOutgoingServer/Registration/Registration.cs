using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectManager.SystemCards;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;

namespace Agora.Outgoing.Server
{
    public sealed partial class ValidationExtension
    {
        /// <summary>
        /// Проверяет учётную запись службы автоматической регистрации.
        /// </summary>
        private bool RegistrationAutomation()
        {
            var config = new XmlDocument();
            config.XmlResolver = null;
            config.Load(@"C:\AGORA-OUT\server\registration-config.xml");
            return String.Equals(config.DocumentElement.GetAttribute("automationAccount"), Request.Session.UserSid.TranslateToName(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Готовит или завершает регистрацию с проверкой подписи и версии.
        /// </summary>
        private string RegistrationAction(Guid cardId, Guid pdfRowId, Guid sourceVersion, bool finalize)
        {
            var xml = new XmlDocument();
            var reply = xml.CreateElement("RegistrationResult");
            xml.AppendChild(reply);
            reply.SetAttribute("status", "error");
            bool ownLock = false;
            bool held = false;
            CardData card = null;
            var registrationMutex = new Mutex(false, @"Global\AgoraOutgoingRegistration-" + cardId.ToString("N"));
            try
            {
                if (!RegistrationAutomation())
                {
                    throw new UnauthorizedAccessException("Операция доступна службе автоматической регистрации.");
                }

                try
                {
                    held = registrationMutex.WaitOne(TimeSpan.FromSeconds(20));
                }
                catch (AbandonedMutexException)
                {
                    held = true;
                }

                if (!held)
                {
                    throw new InvalidOperationException("Регистрация уже выполняется.");
                }

                var session = CreateSession(true);
                card = session.CardManager.GetCardData(cardId);
                if (card.Type.Id != DocumentType)
                {
                    throw new InvalidOperationException("Неподдерживаемый тип документа.");
                }

                if (card.LockStatus == LockStatus.Free)
                {
                    card.PlaceLock();
                    ownLock = true;
                }
                else if (card.LockStatus != LockStatus.OwnerLocked)
                {
                    throw new InvalidOperationException("Карточка занята другим пользователем.");
                }

                card.Refresh();
                if (card.InUpdate)
                {
                    throw new InvalidOperationException("Карточка содержит несохранённые изменения.");
                }

                var sys = card.Sections[SystemSection].FirstRow;
                var main = card.Sections[Main].FirstRow;
                var extra = card.Sections[Extra].FirstRow;
                if (Id(sys, "Kind") != Kind)
                {
                    throw new InvalidOperationException("Неподдерживаемый вид документа.");
                }

                if (Id(sys, "State") == RegisteredState)
                {
                    reply.SetAttribute("status", "unchanged");
                    return xml.OuterXml;
                }

                if (Id(sys, "State") != new Guid("d1754352-3891-46fa-9fd5-61cee8844443"))
                {
                    throw new InvalidOperationException("Документ должен находиться в состоянии «Подписан».");
                }

                if (extra.GetBoolean("ShortRoute").GetValueOrDefault())
                {
                    var sc = DocumentContext(session);
                    var author = sc.GetObject<StaffEmployee>(Id(main, "Author"));
                    if (Id(extra, "ShortRouteConfirmedBy") != Id(main, "Author") || Id(extra, "ShortRouteConfirmedSignature") == Guid.Empty || !sc.GetService<IStaffService>().IsEmployeeInGroup(author, sc.GetObject<StaffGroup>(RegistrarGroup(Id(extra, "Journal"))), true))
                    {
                        throw new InvalidOperationException("Не подтверждены полномочия инициатора-регистратора сокращённого маршрута.");
                    }
                }

                var proof = new XmlDocument();
                proof.LoadXml(ConfirmOutgoingSignature(cardId));
                if (proof.DocumentElement.GetAttribute("status") != "verified")
                {
                    throw new InvalidOperationException(proof.DocumentElement.InnerText);
                }

                Guid version = new Guid(proof.DocumentElement.GetAttribute("fileVersion"));
                Guid journal = Id(extra, "Journal");
                if (extra.GetBoolean("ShortRoute").GetValueOrDefault() && Id(extra, "ShortRouteConfirmedSignature") != new Guid(proof.DocumentElement.GetAttribute("signatureId")))
                {
                    throw new InvalidOperationException("Изменилась подпись сокращённого маршрута.");
                }

                if (finalize && sourceVersion != version)
                {
                    throw new InvalidOperationException("Подписанная версия файла изменилась после формирования PDF.");
                }

                Guid numberRef = Id(main, "RegNumber");
                if (numberRef == Guid.Empty)
                {
                    if (finalize)
                    {
                        throw new InvalidOperationException("Регистрационный номер не выдан.");
                    }

                    var config = new XmlDocument();
                    config.XmlResolver = null;
                    config.Load(@"C:\AGORA-OUT\server\registration-config.xml");
                    var counterNode = config.SelectSingleNode("/RegistrationConfig/Counter[@journal='" + journal + "']") as XmlElement;
                    if (counterNode == null)
                    {
                        throw new InvalidOperationException("Не настроен регистрационный счётчик журнала.");
                    }

                    Guid counterId = new Guid(counterNode.GetAttribute("id"));
                    string code = counterNode.GetAttribute("code");
                    var counter = (NumeratorCard)session.CardManager.GetCard(counterId);
                    if (counter.Name != "АГОРА — Регистрационный — " + code)
                    {
                        throw new InvalidOperationException("Не соответствует регистрационный счётчик.");
                    }

                    var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time")).Date;
                    Guid numericId;
                    int numeric;
                    using (var mutex = new Mutex(false, @"Global\AgoraOutgoingCounter-" + counterId.ToString("N") + "-" + today.Year))
                    {
                        bool counterHeld = false;
                        try
                        {
                            try
                            {
                                counterHeld = mutex.WaitOne(TimeSpan.FromSeconds(20));
                            }
                            catch (AbandonedMutexException)
                            {
                                counterHeld = true;
                            }

                            if (!counterHeld)
                            {
                                throw new InvalidOperationException("Счётчик занят.");
                            }

                            string zoneName = "Y" + today.Year;
                            NumeratorZone zone = null;
                            foreach (NumeratorZone existing in counter.Zones)
                            {
                                if (existing.Name == zoneName)
                                {
                                    zone = existing;
                                    break;
                                }
                            }

                            if (zone == null)
                            {
                                zone = counter.Zones.AddNew(zoneName);
                            }

                            numeric = zone.GetNumber(Guid.Empty, true, out numericId);
                        }
                        finally
                        {
                            if (counterHeld)
                            {
                                mutex.ReleaseMutex();
                            }
                        }
                    }

                    card.BeginUpdate();
                    try
                    {
                        var row = card.Sections[NumbersSection].Rows.AddNew();
                        numberRef = row.Id;
                        row.SetGuid("NumericPart", numericId);
                        row.SetString("Number", Agora.Outgoing.OutgoingNumberFormat.Format(code, false, numeric));
                        main.SetGuid("RegNumber", numberRef);
                        main.SetDateTime("RegDate", today);
                        extra.SetGuid("RegNumberJournal", journal);
                        extra.SetInt32("RegNumberYear", today.Year);
                        card.EndUpdate();
                    }
                    catch
                    {
                        card.CancelUpdate();
                        Trace("Registration numeric reservation retained: " + numericId);
                        throw;
                    }
                }

                if (Id(extra, "RegNumberJournal") != journal || !main.GetDateTime("RegDate").HasValue)
                {
                    throw new InvalidOperationException("Требуется восстановление происхождения регистрационного номера.");
                }

                string number = card.Sections[NumbersSection].Rows[numberRef].GetString("Number");
                reply.SetAttribute("number", number);
                reply.SetAttribute("date", main.GetDateTime("RegDate").Value.ToString("yyyy-MM-dd"));
                reply.SetAttribute("sourceVersion", version.ToString());
                reply.AppendChild(xml.ImportNode(proof.DocumentElement, true));
                if (!finalize)
                {
                    reply.SetAttribute("status", "prepared");
                    return xml.OuterXml;
                }

                var context = DocumentContext(session);
                var document = context.GetObject<Document>(cardId);
                var pdf = document.Files.SingleOrDefault(f => f.GetObjectId() == pdfRowId && f.FileType == DocumentFileType.Additional);
                if (pdf == null || !pdf.FileName.StartsWith("Подписанный вариант", StringComparison.Ordinal) || !pdf.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Не найден дополнительный PDF подписанного варианта.");
                }

                var service = context.GetService<IDocumentService>();
                string download = service.DownloadAdditionalFile(document, pdf);
                try
                {
                    using (var file = File.OpenRead(download))
                    {
                        var header = new byte[5];
                        if (file.Read(header, 0, 5) != 5 || System.Text.Encoding.ASCII.GetString(header) != "%PDF-")
                        {
                            throw new InvalidOperationException("Дополнительный файл не является PDF.");
                        }
                    }
                }
                finally
                {
                    File.Delete(download);
                }

                // Record the derivative provenance before the native state transition. Retries retain the same number.
                card.BeginUpdate();
                try
                {
                    extra.SetGuid("RegistrationPdfRow", pdfRowId);
                    extra.SetGuid("RegistrationPdfVersion", version);
                    card.EndUpdate();
                }
                catch
                {
                    card.CancelUpdate();
                    throw;
                }

                var states = context.GetService<IStateService>();
                var setting = states.GetKindStateSetting(context.GetObject<KindsCardKind>(Kind));
                var branches = setting.StateMachineBranches.Where(b => b.StartState.GetObjectId() == new Guid("d1754352-3891-46fa-9fd5-61cee8844443") && b.EndState.GetObjectId() == RegisteredState && b.Operation.DefaultName == "AGORA.Registration.Complete").ToArray();
                if (branches.Length != 1)
                {
                    throw new InvalidOperationException("Не настроен переход регистрации.");
                }

                states.ChangeState(document, branches[0]);
                context.SaveObject(document);
                context.AcceptChanges();
                reply.SetAttribute("status", "registered");
                TriggerAccessRefresh();
                Trace("Registration completed card=" + cardId + " version=" + version);
                return xml.OuterXml;
            }
            catch (Exception ex)
            {
                Trace("RegistrationAction: " + ex.ToString());
                reply.InnerText = ex.Message;
                return xml.OuterXml;
            }
            finally
            {
                try
                {
                    if (ownLock && card != null)
                    {
                        card.RemoveLock();
                    }
                }
                finally
                {
                    if (held)
                    {
                        registrationMutex.ReleaseMutex();
                    }

                    registrationMutex.Dispose();
                }
            }
        }

        /// <summary>
        /// Проверяет подпись и резервирует реквизиты регистрации письма.
        /// </summary>
        [ExtensionMethod]
        public string PrepareOutgoingRegistration(Guid cardId)
        {
            return RegistrationAction(cardId, Guid.Empty, Guid.Empty, false);
        }

        /// <summary>
        /// Проверяет производный PDF и завершает регистрацию письма.
        /// </summary>
        [ExtensionMethod]
        public string FinalizeOutgoingRegistration(Guid cardId, Guid pdfRowId, Guid sourceVersion)
        {
            return RegistrationAction(cardId, pdfRowId, sourceVersion, true);
        }
    }
}
