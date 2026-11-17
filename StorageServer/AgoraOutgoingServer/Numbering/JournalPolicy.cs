using System;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.StorageServer.Extensibility;
using DocsVision.BackOffice.ObjectModel;

namespace Agora.Outgoing.Server
{
    public sealed partial class ValidationExtension
    {
        /// <summary>
        /// Определяет руководителя инициатора по журналу регистрации.
        /// </summary>
        private static StaffEmployee ResolveInitiatorManager(ObjectContext context, Guid author, Guid journal)
        {
            StaffEmployee manager = null;
            if (journal == new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e"))
            {
                var employee = context.GetObject<StaffEmployee>(author);
                for (var unit = employee.Unit; unit != null; unit = unit.ParentUnit)
                {
                    if (unit.Manager != null)
                    {
                        manager = unit.Manager;
                        break;
                    }
                }
            }
            else
            {
                RegistrarGroup(journal);
                manager = context.GetObject<StaffUnit>(new Guid("98f2635a-02a4-4af7-92ca-f4bb7674434d")).Manager;
            }

            if (manager == null)
            {
                throw new InvalidOperationException("Не настроен руководитель инициатора для выбранного журнала.");
            }

            return manager;
        }

        /// <summary>
        /// Обновляет сохранённого руководителя инициатора.
        /// </summary>
        private static void RefreshStoredManager(UserSession session, ObjectContext context, CardData card)
        {
            var main = card.Sections[Main].FirstRow;
            var extra = card.Sections[Extra].FirstRow;
            var manager = ResolveInitiatorManager(context, Id(main, "Author"), Id(extra, "Journal"));
            if (Id(extra, "InitiatorManager") != manager.GetObjectId())
            {
                bool locked = false;
                try
                {
                    if (card.LockStatus == LockStatus.Free)
                    {
                        card.PlaceLock();
                        locked = true;
                    }
                    else if (card.LockStatus != LockStatus.OwnerLocked && card.LockStatus != LockStatus.OwnerCheckedOut)
                    {
                        throw new InvalidOperationException("Письмо занято другим пользователем.");
                    }

                    card.BeginUpdate();
                    try
                    {
                        extra.SetGuid("InitiatorManager", manager.GetObjectId());
                        card.EndUpdate();
                    }
                    catch
                    {
                        card.CancelUpdate();
                        throw;
                    }
                }
                finally
                {
                    if (locked)
                    {
                        card.RemoveLock();
                    }
                }
            }
        }

        /// <summary>
        /// Проверяет право пользователя изменить журнал регистрации.
        /// </summary>
        private static bool CanEditJournal(ObjectContext context, Guid actor, Guid journal)
        {
            return RegistrarOrDeputy(context, actor, new Guid("4adf3c4e-aae8-466e-be68-91eba2c2dc89")) || (journal != Guid.Empty && RegistrarOrDeputy(context, actor, RegistrarGroup(journal)));
        }

        /// <summary>
        /// Проверяет допустимость изменения журнала исходящего письма.
        /// </summary>
        [ExtensionMethod]
        public string ValidateOutgoingJournalChange(Guid cardId, Guid journal)
        {
            try
            {
                var session = CreateSession(true);
                var c = DocumentContext(session);
                OwnOutgoing(session, c, cardId);
                var raw = session.CardManager.GetCardData(cardId);
                var extra = raw.Sections[Extra].FirstRow;
                Guid existing = Id(extra, "Journal");
                if (existing == journal)
                {
                    return SendingReply("unchanged", "");
                }

                if (Id(raw.Sections[SystemSection].FirstRow, "State") != PreparationState)
                {
                    throw new InvalidOperationException("Журнал можно менять только в подготовке.");
                }

                var actor = NumberingActor(session, Request.Session.UserSid.TranslateToName());
                if (existing == Guid.Empty)
                {
                    Guid author = Id(raw.Sections[Main].FirstRow, "Author");
                    if (author == Guid.Empty)
                    {
                        author = actor;
                    }

                    if (AuthorOrDeputy(session, author, actor))
                    {
                        var employee = c.GetObject<StaffEmployee>(author);
                        for (var unit = employee.Unit; unit != null; unit = unit.ParentUnit)
                        {
                            Guid expected = unit.GetObjectId() == new Guid("4900f7b1-472c-447d-bc0a-7d460937ceb8") ? new Guid("291adc04-cff2-4a23-bb06-4336c1dbb19e") : unit.GetObjectId() == new Guid("691c36db-f7da-4497-9f9b-6930083bf85c") ? new Guid("08137765-5066-4e5d-a189-4f459e32feab") : unit.GetObjectId() == new Guid("617ac655-38d4-4dab-9654-7865dc630b10") ? new Guid("adc70c7d-54d7-4a13-aa6f-2cc208f572b7") : Guid.Empty;
                            if (expected != Guid.Empty)
                            {
                                if (journal == expected)
                                {
                                    return SendingReply("default", "");
                                }

                                break;
                            }
                        }
                    }
                }

                if (!CanEditJournal(c, actor, existing) || !CanEditJournal(c, actor, journal))
                {
                    throw new UnauthorizedAccessException("Изменить журнал может регистратор соответствующего журнала или регистратор ГО.");
                }

                RegistrarGroup(journal);
                ResolveInitiatorManager(c, Id(raw.Sections[Main].FirstRow, "Author"), journal);
                return SendingReply("allowed", "");
            }
            catch (Exception ex)
            {
                return SendingReply("error", ex.Message);
            }
        }
    }
}
