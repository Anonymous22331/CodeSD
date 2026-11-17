using Agora.Outgoing.Workflow;

namespace DVScriptHost
{
    public class DVScript
    {
        /// <summary>
        /// Регистрирует подписанные письма и отправляет уведомления участникам.
        /// </summary>
        public void Execute()
        {
            ProcessHandlers.RegisterAndNotify();
        }
    }
}
