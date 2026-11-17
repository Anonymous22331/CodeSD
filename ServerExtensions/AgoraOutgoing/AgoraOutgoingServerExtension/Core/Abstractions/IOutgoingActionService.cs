using System;
using DocsVision.Platform.WebClient;

namespace Agora.Outgoing.Web
{
    /// <summary>Определяет контракт сервиса исходящего письма.</summary>
    public interface IOutgoingActionService
    {
        /// <summary>Определяет операцию сервиса исходящих писем.</summary>
        OutgoingActionResult Execute(SessionContext context, WebAction request);

    }
}
