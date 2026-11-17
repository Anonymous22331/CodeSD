import type { EventSender, SavingArguments } from "../Models/AgoraLayout";
import { $OutgoingUi } from "../Services/Abstractions/IOutgoingUiService";
import { callOperation } from "../Services/OutgoingController";
import { applyContext, getControl, getLayout, getReferenceId, getValue, hasFlag, isEditing, isUnsaved, loadCreationDefaults, showError } from "../Services/LayoutService";

/** Заполняет карточку и применяет серверные права при открытии разметки. */
export async function AgoraOpened(sender: EventSender): Promise<void>
{
    const layout = getLayout(sender);

    try
    {
        if (isUnsaved(layout))
        {
            await loadCreationDefaults(layout);
            return;
        }

        const context = await callOperation(layout, "GetOutgoingWebContext", { Initialize: isEditing(layout) });
        applyContext(layout, context);

        if (isEditing(layout) && hasFlag(context, "canStart") && !context.getAttribute("temporaryNumber"))
        {
            await callOperation(layout, "AssignDraftNumber");
            await layout.reloadFromServer();
            return;
        }

        if (hasFlag(context, "initialized"))
        {
            await layout.reloadFromServer();
        }
    }
    catch (error)
    {
        await showError(layout, error);
    }
}

/** Проверяет обязательные поля и допустимость изменения журнала до сохранения. */
export async function AgoraSaving(sender: EventSender, args: SavingArguments): Promise<void>
{
    const layout = getLayout(sender);
    const errors: string[] = [];
    const sheets = getValue(layout, "agoraSheets");

    if (!String(getValue(layout, "agoraTheme") || "").trim())
    {
        errors.push("Заполните тему письма.");
    }

    if (!String(getValue(layout, "agoraContent") || "").trim())
    {
        errors.push("Заполните содержание письма.");
    }

    if (sheets !== null && sheets !== undefined && sheets !== "" && (!Number.isInteger(Number(sheets)) || Number(sheets) < 0))
    {
        errors.push("Количество листов должно быть целым неотрицательным числом.");
    }

    const required = [
        ["agoraAuthor", "Инициатор"], ["agoraManager", "Руководитель инициатора"],
        ["agoraRegistrationPlace", "Место регистрации"], ["agoraJournal", "Вид журнала"],
        ["agoraDocumentType", "Тип документа"], ["agoraSigningMethod", "Способ подписания"]
    ];

    for (const [name, caption] of required)
    {
        if (!getValue(layout, name))
        {
            errors.push(`Заполните поле «${caption}».`);
        }
    }

    const signers = getValue(layout, "agoraSigner");
    if (!signers || (Array.isArray(signers) && signers.length !== 1))
    {
        errors.push("Выберите одного подписанта письма.");
    }

    if (!getControl(layout, "agoraRecipients")?.params.rows?.length)
    {
        errors.push("Выберите организацию-получателя.");
    }

    const hasMainFile = getControl(layout, "fileList")?.params.files?.some(file => file.data?.isMain === true);
    if (layout.__agoraSavePurpose !== "GenerateMainLetter" && !hasMainFile)
    {
        errors.push("Сформируйте или добавьте основной файл письма.");
    }

    if (errors.length > 0)
    {
        args.cancel();
        await layout.getService($OutgoingUi).showError(errors.join("\n"));
        return;
    }

    if (isUnsaved(layout))
    {
        return;
    }

    args.wait();
    try
    {
        await callOperation(layout, "ValidateOutgoingJournalChange", {
            Journal: getReferenceId(getValue(layout, "agoraJournal"))
        });
        args.accept();
    }
    catch (error)
    {
        args.cancel();
        await showError(layout, error);
    }
}

/** Выдаёт временный номер после сохранения и обновляет серверный контекст. */
export async function AgoraSaved(sender: EventSender): Promise<void>
{
    const layout = getLayout(sender);
    layout.__agoraPersisted = true;

    if (layout.__agoraNumbering)
    {
        return;
    }

    layout.__agoraNumbering = true;

    try
    {
        const context = await callOperation(layout, "GetOutgoingWebContext", { Initialize: true });
        applyContext(layout, context);

        if (hasFlag(context, "canNumber"))
        {
            const result = await callOperation(layout, "AssignDraftNumber");

            if (!layout.__agoraExecutingAction && (result.getAttribute("status") !== "unchanged" || hasFlag(context, "initialized")))
            {
                await layout.reloadFromServer();
            }
        }
    }
    catch (error)
    {
        await showError(layout, error);
    }
    finally
    {
        layout.__agoraNumbering = false;
    }
}
