import type { EventSender } from "../Models/AgoraLayout";
import { callOperation } from "../Services/OutgoingController";
import { getLayout, getValue, hasFlag, isUnsaved, loadCreationDefaults, saveIfEditing, showError, updateRouteCaption } from "../Services/LayoutService";

/** Сохраняет документ, выполняет серверную операцию и обновляет карточку. */
async function executeAction(sender: EventSender, method: string): Promise<void>
{
    const layout = getLayout(sender);
    layout.__agoraExecutingAction = true;
    layout.__agoraSavePurpose = method;

    try
    {
        if (method !== "CancelOutgoingDocument")
        {
            await saveIfEditing(layout);
        }

        await callOperation(layout, method);
        await layout.reloadFromServer();
    }
    catch (error)
    {
        showError(error);
    }
    finally
    {
        layout.__agoraExecutingAction = false;
        layout.__agoraSavePurpose = null;
    }
}

/** Формирует основной файл письма по настроенному шаблону. */
export function AgoraGenerate(sender: EventSender): Promise<void>
{
    return executeAction(sender, "GenerateMainLetter");
}

/** Запускает согласование или сокращённую регистрацию. */
export function AgoraStart(sender: EventSender): Promise<void>
{
    return executeAction(sender, getValue(getLayout(sender), "agoraShortRoute") ? "BeginShortRegistration" : "StartOutgoingApproval");
}

/** Фиксирует факт отправки письма. */
export function AgoraSend(sender: EventSender): Promise<void>
{
    return executeAction(sender, "MarkOutgoingSent");
}

/** Запрашивает подтверждение и отменяет подготовку письма. */
export async function AgoraCancel(sender: EventSender): Promise<void>
{
    if (window.confirm("Отменить подготовку этого письма?"))
    {
        await executeAction(sender, "CancelOutgoingDocument");
    }
}

/** Сохраняет текущую карточку средствами платформы. */
export async function AgoraSave(sender: EventSender): Promise<void>
{
    try
    {
        await getLayout(sender).saveCard();
    }
    catch (error)
    {
        showError(error);
    }
}

/** Обновляет подпись кнопки при изменении сокращённого маршрута. */
export function AgoraShortChanged(sender: EventSender): void
{
    updateRouteCaption(getLayout(sender));
}

/** Обновляет зависимые значения при выборе другого журнала. */
export async function AgoraJournalChanged(sender: EventSender): Promise<void>
{
    const layout = getLayout(sender);

    try
    {
        if (isUnsaved(layout))
        {
            await loadCreationDefaults(layout);
        }
        else if (layout.__agoraContext && hasFlag(layout.__agoraContext, "draftEdit"))
        {
            layout.__agoraJournalChanged = true;
        }
    }
    catch (error)
    {
        showError(error);
    }
}
