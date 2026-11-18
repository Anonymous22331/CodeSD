namespace Agora.Outgoing.Workflow
{
    /// <summary>
    /// Предоставляет точки входа для программных узлов бизнес-процессов.
    /// </summary>
    public static class ProcessHandlers
    {
        /// <summary>
        /// Регистрирует подписанные письма и уведомляет участников.
        /// </summary>
        public static void RegisterAndNotify()
        {
            PlatformAssemblyResolver.Initialize();
            Runtime.Run("delivery");
        }

        /// <summary>
        /// Архивирует отправленные письма после истечения установленного срока.
        /// </summary>
        public static void ArchiveSentDocuments()
        {
            PlatformAssemblyResolver.Initialize();
            Runtime.Run("archive");
        }
    }
}
