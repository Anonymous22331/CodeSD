using Agora.Outgoing.Workflow;

namespace DVScriptHost
{
    public class DVScript
    {
        /// <summary>
        /// Переводит отправленные письма в архив по установленному сроку.
        /// </summary>
        public void Execute()
        {
            ProcessHandlers.ArchiveSentDocuments();
        }
    }
}
