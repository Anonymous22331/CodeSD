using System;
using System.Web.Mvc;
using DocsVision.Platform.WebClient;
using DocsVision.Platform.WebClient.Helpers;
using DocsVision.Platform.WebClient.Models.Generic;

namespace Agora.Outgoing.Web
{
    /// <summary>Принимает HTTP-запросы и вызывает сервисы исходящих писем.</summary>
    public sealed class AgoraOutgoingController : Controller
    {
        private readonly ICurrentObjectContextProvider contextProvider;
        private readonly IOutgoingCreationService creationService;
        private readonly IOutgoingActionService actionService;

        /// <summary>Принимает HTTP-запросы и вызывает сервисы исходящих писем.</summary>
        public AgoraOutgoingController(ICurrentObjectContextProvider contextProvider,
            IOutgoingCreationService creationService, IOutgoingActionService actionService)
        {
            this.contextProvider = contextProvider;
            this.creationService = creationService;
            this.actionService = actionService;
        }

        /// <summary>Проверяет запрос и получает контекст текущего пользователя.</summary>
        private SessionContext GetContext()
        {
            if (Request.Headers["X-Agora-Web"] != "1")
            {
                throw new UnauthorizedAccessException("Отсутствует заголовок запроса Web-клиента.");
            }

            SessionContext context = contextProvider.GetOrCreateCurrentSessionContext();
            if (context == null || context.Session == null)
            {
                throw new UnauthorizedAccessException("Выполните вход в Web-клиент.");
            }

            return context;
        }

        /// <summary>Выполняет разрешенную операцию исходящего письма.</summary>
        [HttpPost]
        public ContentResult Execute(WebAction request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            SessionContext context = GetContext();
            if (request.Method == "GetOutgoingCreationDefaults")
            {
                var defaultsResponse = new CommonResponse<CreationDefaults>();
                defaultsResponse.InitializeSuccess(creationService.GetDefaults(context, request.Journal));
                return Content(JsonHelper.SerializeToJson(defaultsResponse), "application/json");
            }

            OutgoingActionResult result = actionService.Execute(context, request);
            var response = new CommonResponse<OutgoingActionResult>();
            if (request.CardId != Guid.Empty)
            {
                response.InitializeSuccess(context.AdvancedCardManager.GetCardTimestampModel(request.CardId, true), result);
            }
            else
            {
                response.InitializeSuccess(result);
            }

            return Content(JsonHelper.SerializeToJson(response), "application/json");
        }

    }
}
