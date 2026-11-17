namespace Agora.Outgoing.Web
{
    /// <summary>Описывает значение стандартного справочного контрола Web-клиента.</summary>
    public sealed class EmployeeFieldValue
    {
        public string Id
        {
            get; set;
        }
        public int DataType
        {
            get; set;
        }
        public string DisplayName
        {
            get; set;
        }
        public string FirstName
        {
            get; set;
        }
        public string LastName
        {
            get; set;
        }
        public string MiddleName
        {
            get; set;
        }
        public string AccountName
        {
            get; set;
        }
        public string UnitId
        {
            get; set;
        }
        public string DepartmentName
        {
            get; set;
        }
        public bool IsCurrent
        {
            get; set;
        }
        public int Status
        {
            get; set;
        }
    }
}
