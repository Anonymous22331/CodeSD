using System;

namespace Agora.Outgoing.Web
{
    public sealed class WebAction
    {
        public Guid Journal
        {
            get; set;
        }
        public Guid CardId
        {
            get; set;
        }
        public string Method
        {
            get; set;
        }
        public Guid TaskId
        {
            get; set;
        }
        public int Semantic
        {
            get; set;
        }
        public string Comment
        {
            get; set;
        }
        public string RecipientEmail
        {
            get; set;
        }
        public string Message
        {
            get; set;
        }
        public bool Initialize
        {
            get; set;
        }
    }
}
