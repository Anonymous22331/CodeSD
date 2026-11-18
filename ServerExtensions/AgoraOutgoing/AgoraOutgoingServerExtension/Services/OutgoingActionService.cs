using System;
using System.Linq;
using DocsVision.Platform.WebClient;
using DocsVision.Platform.ObjectManager;

namespace Agora.Outgoing.Web
{
    /// <summary>Выполняет разрешенную операцию исходящего письма.</summary>
    public sealed class OutgoingActionService : IOutgoingActionService
    {
        /// <summary>Выполняет разрешенную операцию исходящего письма.</summary>
        public OutgoingActionResult Execute(SessionContext context, WebAction request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            string name = request.Method;

            var allowed = new[]
            {
                "ValidateOutgoingJournalChange",
                "CancelOutgoingDocument",
                "GetOutgoingWebContext",
                "AssignDraftNumber",
                "GenerateMainLetter",
                "StartOutgoingApproval",
                "BeginShortRegistration",
                "GetOutgoingTaskActions",
                "CompleteOutgoingTask",
                "MarkOutgoingSent",
                "GetOutgoingDocumentOperations",
                "GetOutgoingApprovalHistory",
                "CreateOutgoingFamiliarizationDraft",
                "SendOutgoingDocumentMessage"
            };
            if (!allowed.Contains(name))
            {
                throw new UnauthorizedAccessException("Неподдерживаемая операция.");
            }

            var method = context.Session.ExtensionManager.GetExtensionMethod("AgoraOutgoingValidation", name);
            if (name == "CompleteOutgoingTask")
            {
                method.Parameters.AddNew("taskId", ParameterValueType.Guid, request.TaskId);
                method.Parameters.AddNew("semantic", ParameterValueType.Integer, request.Semantic);
                method.Parameters.AddNew("comment", ParameterValueType.String, request.Comment ?? "");
            }
            else
            {
                method.Parameters.AddNew("cardId", ParameterValueType.Guid, request.CardId);
                if (name == "ValidateOutgoingJournalChange")
                {
                    method.Parameters.AddNew("journal", ParameterValueType.Guid, request.Journal);
                }

                if (name == "GetOutgoingWebContext")
                {
                    method.Parameters.AddNew("initialize", ParameterValueType.Bool, request.Initialize);
                }

                if (name == "SendOutgoingDocumentMessage")
                {
                    method.Parameters.AddNew("recipientEmail", ParameterValueType.String, request.RecipientEmail ?? "");
                    method.Parameters.AddNew("message", ParameterValueType.String, request.Message ?? "");
                }
            }

            string result = Convert.ToString(method.Execute());
            return new OutgoingActionResult { Xml = result };
        }

    }
}
