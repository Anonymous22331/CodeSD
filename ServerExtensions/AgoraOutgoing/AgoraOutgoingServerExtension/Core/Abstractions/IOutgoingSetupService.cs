using System;
using DocsVision.Platform.WebClient;

namespace Agora.Outgoing.Web
{
    /// <summary>Определяет контракт сервиса исходящего письма.</summary>
    public interface IOutgoingSetupService
    {
        /// <summary>Определяет операцию сервиса исходящих писем.</summary>
        object PatchCreationLayout(SessionContext context);

        /// <summary>Определяет операцию сервиса исходящих писем.</summary>
        object Bootstrap(SessionContext context, bool standardOnly);

    }
}
