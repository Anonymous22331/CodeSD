import type { ActionChoice, AgoraLayout, DialogResult, EventSender } from "../Models/AgoraLayout";
import { showActionDialog } from "../Services/ActionDialog";
import { callOperation } from "../Services/OutgoingController";
import { getLayout, hasFlag, saveIfEditing, showError } from "../Services/LayoutService";

/** Открывает карточку или задание штатным маршрутом Web-клиента. */
function openCard(id: string | null): void
{
    if (!id)
    {
        throw new Error("Сервер не вернул идентификатор карточки.");
    }

    window.location.hash = "#/CardView/" + encodeURIComponent(id);
}

/** Получает доступные задания, решения и операции документа. */
async function loadChoices(layout: AgoraLayout): Promise<ActionChoice[]>
{
    const [tasks, operations] = await Promise.all([
        callOperation(layout, "GetOutgoingTaskActions"),
        callOperation(layout, "GetOutgoingDocumentOperations")
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
        openCard(choice.cardId);
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

        await callOperation(layout, "CompleteOutgoingTask", { TaskId: choice.taskId, Semantic: choice.semantic, Comment: result.comment });
        await layout.reloadFromServer();
        return;
    }

    if (choice.operation === 27 || choice.operation === 35)
    {
        const response = await callOperation(layout, choice.operation === 27 ? "CreateOutgoingFamiliarizationDraft" : "GetOutgoingApprovalHistory");
        openCard(response.getAttribute("reconcile"));
    }
    else if (choice.operation === 28)
    {
        const recipient = window.prompt("Адрес получателя в тестовом домене agora.test");
        if (!recipient)
        {
            return;
        }

        const message = result.comment || window.prompt("Текст сообщения");
        if (message)
        {
            const response = await callOperation(layout, "SendOutgoingDocumentMessage", { RecipientEmail: recipient, Message: message });
            window.alert(response.textContent);
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
        const result = await showActionDialog("Действия по исходящему письму", choices);

        if (result)
        {
            await executeChoice(layout, result);
        }
    }
    catch (error)
    {
        showError(error);
    }
}
