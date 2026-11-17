namespace Agora.Outgoing.Web
{
    /// <summary>Определяет контракт сервиса исходящего письма.</summary>
    public sealed class CreationDefaults
    {
        public EmployeeFieldValue Author
        {
            get; set;
        }
        public UnitFieldValue Unit
        {
            get; set;
        }
        public EmployeeFieldValue Manager
        {
            get; set;
        }
        public DirectoryFieldValue Journal
        {
            get; set;
        }
        public DirectoryFieldValue DocumentType
        {
            get; set;
        }
        public DirectoryFieldValue Delivery
        {
            get; set;
        }
        public DirectoryFieldValue Signing
        {
            get; set;
        }
        public bool ShortAllowed
        {
            get; set;
        }
    }
}
