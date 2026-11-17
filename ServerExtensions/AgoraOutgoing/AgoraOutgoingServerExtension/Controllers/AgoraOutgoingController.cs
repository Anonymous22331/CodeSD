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
        private readonly IOutgoingSetupService setupService;

        /// <summary>Принимает HTTP-запросы и вызывает сервисы исходящих писем.</summary>
        public AgoraOutgoingController(ICurrentObjectContextProvider contextProvider,
            IOutgoingCreationService creationService, IOutgoingActionService actionService,
            IOutgoingSetupService setupService)
        {
            this.contextProvider = contextProvider;
            this.creationService = creationService;
            this.actionService = actionService;
            this.setupService = setupService;
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
                return Success(creationService.GetDefaults(context, request.Journal));
            }

            OutgoingActionResult result = actionService.Execute(context, request);
            var response = new CommonResponse<OutgoingActionResult>();
            if (request.CardId != Guid.Empty)
            {
                context.AdvancedCardManager.GetCardData(request.CardId, true).Refresh();
                response.InitializeSuccess(context.AdvancedCardManager.GetCardTimestampModel(request.CardId, true), result);
            }
            else
            {
                response.InitializeSuccess(result);
            }

            return Content(JsonHelper.SerializeToJson(response), "application/json");
        }

        /// <summary>Импортирует разметку после создания резервной копии.</summary>
        [HttpPost]
        public ContentResult PatchCreationLayout()
        {
            return Success(setupService.PatchCreationLayout(GetContext()));
        }

        /// <summary>Настраивает решения, операции и ролевую модель.</summary>
        [HttpPost]
        public ContentResult Bootstrap(bool standardOnly = false)
        {
            Server.ScriptTimeout = 1800;
            return Success(setupService.Bootstrap(GetContext(), standardOnly));
        }

        /// <summary>Сериализует данные в стандартный ответ Web-клиента.</summary>
        private ContentResult Success<T>(T data)
        {
            var response = new CommonResponse<T>();
            response.InitializeSuccess(data);
            return Content(JsonHelper.SerializeToJson(response), "application/json");
        }
    }
}
