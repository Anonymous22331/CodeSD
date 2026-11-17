using System;
using DocsVision.Platform.WebClient;

namespace Agora.Outgoing.Web
{
    /// <summary>Определяет контракт сервиса исходящего письма.</summary>
    public interface IOutgoingCreationService
    {
        /// <summary>Определяет операцию сервиса исходящих писем.</summary>
        CreationDefaults GetDefaults(SessionContext context, Guid selectedJournal);

    }
}
