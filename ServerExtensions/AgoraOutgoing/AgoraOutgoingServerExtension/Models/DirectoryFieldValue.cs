namespace Agora.Outgoing.Web
{
    /// <summary>Описывает значение стандартного справочного контрола Web-клиента.</summary>
    public sealed class DirectoryFieldValue
    {
        public string Id
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }
        public string Description
        {
            get; set;
        }
        public string ParentId
        {
            get; set;
        }
        public int Type
        {
            get; set;
        }
        public int DataType
        {
            get; set;
        }
        public bool HasChildren
        {
            get; set;
        }
    }
}
