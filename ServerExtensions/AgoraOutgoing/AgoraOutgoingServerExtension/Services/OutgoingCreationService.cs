using System;
using DocsVision.Platform.WebClient;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;

namespace Agora.Outgoing.Web
{
    /// <summary>Определяет начальные значения по сотруднику и выбранному журналу.</summary>
    public sealed class OutgoingCreationService : IOutgoingCreationService
    {
        /// <summary>Возвращает начальные значения без сохранения карточки.</summary>
        public CreationDefaults GetDefaults(SessionContext context, Guid selectedJournal)
        {
            var staff = context.ObjectContext.GetService<IStaffService>();
            var employee = staff.GetCurrentEmployee();
            if (employee == null)
            {
                throw new InvalidOperationException("Текущая учетная запись не связана с сотрудником.");
            }

            var unit = employee.Unit;
            if (unit == null)
            {
                throw new InvalidOperationException("У сотрудника не задано подразделение.");
            }

            Guid journal = selectedJournal == Guid.Empty ? FindJournal(unit) : selectedJournal;
            Guid registrar = GetRegistrarGroup(journal);
            var manager = FindManager(context, unit, journal);
            return new CreationDefaults
            {
                Author = EmployeeValue(employee),
                Unit = new UnitFieldValue
                {
                    Id = unit.GetObjectId().ToString(),
                    Name = unit.Name,
                    FullName = unit.Name,
                    DataType = 16,
                    DepartmentType = 1
                },
                Manager = EmployeeValue(manager),
                Journal = journal == Guid.Empty ? null : DirectoryValue(journal, GetJournalName(journal), OutgoingDirectoryIds.JournalDirectory),
                DocumentType = DirectoryValue(OutgoingDirectoryIds.InformationMessage, "Информационное сообщение", Guid.Empty),
                Delivery = DirectoryValue(OutgoingDirectoryIds.EmailDelivery, "Электронная почта", Guid.Empty),
                Signing = DirectoryValue(OutgoingDirectoryIds.PaperSigning, "Бумага", Guid.Empty),
                ShortAllowed = registrar != Guid.Empty && staff.IsEmployeeInGroup(employee, context.ObjectContext.GetObject<StaffGroup>(registrar), true)
            };
        }

        /// <summary>Находит журнал по ближайшему известному подразделению в иерархии.</summary>
        private Guid FindJournal(StaffUnit unit)
        {
            for (var current = unit; current != null; current = current.ParentUnit)
            {
                Guid id = current.GetObjectId();
                if (id == OutgoingDirectoryIds.HeadOfficeUnit)
                {
                    return OutgoingDirectoryIds.HeadOfficeJournal;
                }
                if (id == OutgoingDirectoryIds.KazanUnit)
                {
                    return OutgoingDirectoryIds.KazanJournal;
                }
                if (id == OutgoingDirectoryIds.SaratovUnit)
                {
                    return OutgoingDirectoryIds.SaratovJournal;
                }
            }
            return Guid.Empty;
        }

        /// <summary>Возвращает группу регистратора и отклоняет неизвестный журнал.</summary>
        private Guid GetRegistrarGroup(Guid journal)
        {
            if (journal == Guid.Empty)
            {
                return Guid.Empty;
            }
            if (journal == OutgoingDirectoryIds.HeadOfficeJournal)
            {
                return OutgoingDirectoryIds.HeadOfficeRegistrars;
            }
            if (journal == OutgoingDirectoryIds.KazanJournal)
            {
                return OutgoingDirectoryIds.KazanRegistrars;
            }
            if (journal == OutgoingDirectoryIds.SaratovJournal)
            {
                return OutgoingDirectoryIds.SaratovRegistrars;
            }
            throw new InvalidOperationException("Выбран неизвестный журнал исходящих.");
        }

        /// <summary>Выбирает руководителя по правилам центрального офиса и филиалов.</summary>
        private StaffEmployee FindManager(SessionContext context, StaffUnit unit, Guid journal)
        {
            if (journal == OutgoingDirectoryIds.HeadOfficeJournal)
            {
                for (var current = unit; current != null; current = current.ParentUnit)
                {
                    if (current.Manager != null)
                    {
                        return current.Manager;
                    }
                }
                return null;
            }
            return journal == Guid.Empty ? null : context.ObjectContext.GetObject<StaffUnit>(OutgoingDirectoryIds.BranchManagementUnit).Manager;
        }

        /// <summary>Возвращает отображаемый код известного журнала.</summary>
        private string GetJournalName(Guid journal)
        {
            if (journal == OutgoingDirectoryIds.HeadOfficeJournal)
            {
                return "ГО";
            }
            return journal == OutgoingDirectoryIds.KazanJournal ? "16" : "64";
        }

        /// <summary>Формирует значение стандартного контрола сотрудника.</summary>
        private EmployeeFieldValue EmployeeValue(StaffEmployee employee)
        {
            if (employee == null)
            {
                return null;
            }
            return new EmployeeFieldValue
            {
                Id = employee.GetObjectId().ToString(),
                DataType = 64,
                DisplayName = (employee.LastName + " " + employee.FirstName + " " + employee.MiddleName).Trim(),
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                MiddleName = employee.MiddleName,
                AccountName = employee.AccountName,
                UnitId = employee.Unit == null ? Guid.Empty.ToString() : employee.Unit.GetObjectId().ToString(),
                DepartmentName = employee.Unit == null ? "" : employee.Unit.Name,
                IsCurrent = false,
                Status = 0
            };
        }

        /// <summary>Формирует значение стандартного контрола универсального справочника.</summary>
        private DirectoryFieldValue DirectoryValue(Guid id, string name, Guid parent)
        {
            return new DirectoryFieldValue
            {
                Id = id.ToString(),
                Name = name,
                Description = "",
                ParentId = parent.ToString(),
                Type = 2,
                DataType = 0,
                HasChildren = false
            };
        }
    }
}
