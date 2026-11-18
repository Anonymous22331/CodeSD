import type { ActionChoice, AgoraLayout, DialogResult, EventSender } from "../Models/AgoraLayout";
import { $OutgoingUi } from "../Services/Abstractions/IOutgoingUiService";
import { $Outgoing } from "../Services/Abstractions/IOutgoingService";
import { getLayout, hasFlag, saveIfEditing, showError } from "../Helpers/LayoutHelpers";

/** Получает доступные задания, решения и операции документа. */
async function loadChoices(layout: AgoraLayout): Promise<ActionChoice[]>
{
    const [tasks, operations] = await Promise.all([
        layout.getService($Outgoing).execute(layout.cardInfo.id, "GetOutgoingTaskActions"),
        layout.getService($Outgoing).execute(layout.cardInfo.id, "GetOutgoingDocumentOperations")
    ]);
    const choices: ActionChoice[] = [];

    for (const task of Array.from(tasks.getElementsByTagName("Task")))
    {
        const taskId = task.getAttribute("id") || "";
        const name = task.getAttribute("name") || "";
        choices.push({ kind: "open", label: hasFlag(task, "signing") ? "Подписать ЭП — открыть штатное задание" : "Открыть задание: " + name, cardId: taskId });

        for (const decision of Array.from(task.getElementsByTagName("Decision")))
        {
            choices.push({
                kind: "decision", label: name + ": " + decision.getAttribute("name"), taskId,
                semantic: Number(decision.getAttribute("semantic")), save: decision.getAttribute("save") !== "false",
                required: hasFlag(decision, "comment")
            });
        }
    }

    for (const operation of Array.from(operations.getElementsByTagName("Operation")))
    {
        choices.push({ kind: "operation", label: operation.getAttribute("name") || "", operation: Number(operation.getAttribute("row")) });
    }

    if (choices.length === 0)
    {
        throw new Error("Нет доступных действий в текущем состоянии.");
    }

    return choices;
}

/** Выполняет выбранное решение по заданию или операцию документа. */
async function executeChoice(layout: AgoraLayout, result: DialogResult): Promise<void>
{
    const choice = result.choice;

    if (choice.kind === "open")
    {
        await layout.getService($OutgoingUi).openCard(choice.cardId);
        return;
    }

    if (choice.kind === "decision")
    {
        if (choice.required && !result.comment.trim())
        {
            throw new Error("Укажите комментарий к решению.");
        }

        const context = layout.__agoraContext;
        if (choice.save && context && (hasFlag(context, "draftEdit") || hasFlag(context, "controlEdit")))
        {
            await saveIfEditing(layout);
        }

        await layout.getService($Outgoing).execute(layout.cardInfo.id, "CompleteOutgoingTask", { TaskId: choice.taskId, Semantic: choice.semantic, Comment: result.comment });
        await layout.reloadFromServer();
        return;
    }

    if (choice.operation === 27 || choice.operation === 35)
    {
        const response = await layout.getService($Outgoing).execute(layout.cardInfo.id, choice.operation === 27 ? "CreateOutgoingFamiliarizationDraft" : "GetOutgoingApprovalHistory");
        await layout.getService($OutgoingUi).openCard(response.getAttribute("reconcile") || "");
    }
    else if (choice.operation === 28)
    {
        const recipient = await layout.getService($OutgoingUi).input("Адрес получателя в тестовом домене agora.test");
        if (!recipient)
        {
            return;
        }

        const message = result.comment || await layout.getService($OutgoingUi).input("Текст сообщения", true);
        if (message)
        {
            const response = await layout.getService($Outgoing).execute(layout.cardInfo.id, "SendOutgoingDocumentMessage", { RecipientEmail: recipient, Message: message });
            await layout.getService($OutgoingUi).showInfo(response.textContent || "Сообщение отправлено.");
        }
    }
}

/** Показывает доступные действия и выполняет выбор пользователя. */
export async function AgoraTasks(sender: EventSender): Promise<void>
{
    const layout = getLayout(sender);

    try
    {
        const choices = await loadChoices(layout);
        const result = await layout.getService($OutgoingUi).chooseAction("Действия по исходящему письму", choices);

        if (result)
        {
            await executeChoice(layout, result);
        }
    }
    catch (error)
    {
        await showError(layout, error);
    }
}
