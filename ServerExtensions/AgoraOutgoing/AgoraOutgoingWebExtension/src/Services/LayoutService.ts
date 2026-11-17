import type { AgoraControl, AgoraLayout, DirectoryValue, EventSender } from "../Models/AgoraLayout";
import { getCreationDefaults } from "./OutgoingController";

/** Получает разметку из аргумента события платформы. */
export function getLayout(sender: EventSender): AgoraLayout
{
    return "controls" in sender && "cardInfo" in sender ? sender : sender.layout;
}

/** Находит стандартный контрол по имени из конструктора разметок. */
export function getControl(layout: AgoraLayout, name: string): AgoraControl | undefined
{
    const item = layout.controls.tryGet ? layout.controls.tryGet(name) : layout.controls[name];
    return item as unknown as AgoraControl | undefined;
}

/** Читает значение поля карточки. */
export function getValue(layout: AgoraLayout, name: string): unknown
{
    return getControl(layout, name)?.params.value ?? null;
}

/** Извлекает идентификатор из строкового или справочного значения. */
export function getReferenceId(value: unknown): string | undefined
{
    if (typeof value === "string")
    {
        return value;
    }

    if (value && typeof value === "object")
    {
        const reference = value as DirectoryValue;
        return reference.id || reference.itemId;
    }

    return undefined;
}

/** Определяет, открыта ли разметка для редактирования. */
export function isEditing(layout: AgoraLayout): boolean
{
    return !(layout.layoutInfo && layout.layoutInfo.action === 0);
}

/** Настраивает доступность редактирования с учётом режима просмотра. */
function setEnabled(layout: AgoraLayout, name: string, allowed: boolean): void
{
    const item = getControl(layout, name);

    if (item)
    {
        const editable = allowed && isEditing(layout);
        item.params.editMode = editable ? 2 : 1;
        item.params.disabled = !editable;
    }
}

/** Изменяет видимость команды или поля. */
export function setVisible(layout: AgoraLayout, name: string, allowed: boolean): void
{
    const item = getControl(layout, name);

    if (item)
    {
        item.params.visibility = allowed;
    }
}

/** Читает логический признак из серверного контекста карточки. */
export function hasFlag(context: Element, name: string): boolean
{
    return context.getAttribute(name) === "true";
}

/** Обновляет подпись команды с учётом сокращённого маршрута. */
export function updateRouteCaption(layout: AgoraLayout): void
{
    const button = getControl(layout, "agoraStart");

    if (button)
    {
        button.params.text = getValue(layout, "agoraShortRoute") ? "Зарегистрировать" : "На согласование";
    }
}

/** Применяет права и доступные действия из серверного контекста. */
export function applyContext(layout: AgoraLayout, context: Element): void
{
    layout.__agoraContext = context;
    const draftEdit = hasFlag(context, "draftEdit");
    const controlEdit = hasFlag(context, "controlEdit");
    const shippingEdit = hasFlag(context, "shippingEdit");

    for (const name of ["agoraTheme", "agoraDocumentType", "agoraSheets", "agoraApprovers", "agoraRecipients"])
    {
        setEnabled(layout, name, draftEdit);
    }

    for (const name of ["agoraSigner", "agoraContent"])
    {
        setEnabled(layout, name, draftEdit || controlEdit);
    }

    setEnabled(layout, "agoraJournal", hasFlag(context, "journalEdit"));
    setEnabled(layout, "agoraShortRoute", hasFlag(context, "shortAllowed"));

    for (const name of ["agoraDeliveryMethod", "agoraSendingDate", "agoraSendingComment"])
    {
        setEnabled(layout, name, shippingEdit);
    }

    for (const name of ["agoraAuthor", "agoraManager", "agoraRegistrationPlace", "agoraSigningMethod", "agoraRegNumber", "agoraRegDate", "agoraTemporaryNumber"])
    {
        setEnabled(layout, name, false);
    }

    setVisible(layout, "agoraGenerate", hasFlag(context, "canGenerate"));
    setVisible(layout, "agoraStart", hasFlag(context, "canStart"));
    setVisible(layout, "agoraSend", shippingEdit);
    setVisible(layout, "agoraCancel", hasFlag(context, "canCancel"));
    setVisible(layout, "agoraSave", isEditing(layout) && (draftEdit || hasFlag(context, "journalEdit") || controlEdit || shippingEdit));

    const number = getControl(layout, "agoraTemporaryNumber");
    if (number)
    {
        number.params.value = context.getAttribute("temporaryNumber") || "";
    }

    const created = getControl(layout, "agoraCreatedAt");
    if (created)
    {
        const date = new Date(context.getAttribute("createdAt") || "");
        created.params.text = "Дата создания: " + (Number.isNaN(date.getTime()) ? "" : date.toLocaleString("ru-RU"));
    }

    updateRouteCaption(layout);
}

/** Показывает пользователю ошибку операции. */
export function showError(error: unknown): void
{
    window.alert(error instanceof Error ? error.message : String(error));
}

/** Определяет, что новая карточка ещё не сохранена в базе. */
export function isUnsaved(layout: AgoraLayout): boolean
{
    return !layout.__agoraPersisted && /^#\/NewCard\//i.test(window.location.hash);
}

/** Заполняет пустое справочное поле или принудительно обновляет значение. */
function setDefault(layout: AgoraLayout, name: string, model: DirectoryValue | undefined, replace = false): void
{
    const item = getControl(layout, name);
    const current = item?.params.value;

    if (item && model && (replace || !current || (Array.isArray(current) && current.length === 0)))
    {
        item.params.value = item.params.multipleSelection ? [model] : model;
    }
}

/** Заполняет инициатора, подразделение, журнал и остальные начальные значения. */
export async function loadCreationDefaults(layout: AgoraLayout): Promise<void>
{
    const defaults = await getCreationDefaults(getReferenceId(getValue(layout, "agoraJournal")));
    setDefault(layout, "agoraAuthor", defaults.author);
    setDefault(layout, "agoraRegistrationPlace", defaults.unit);
    setDefault(layout, "agoraJournal", defaults.journal);
    setDefault(layout, "agoraManager", defaults.manager, true);
    setDefault(layout, "agoraDocumentType", defaults.documentType);
    setDefault(layout, "agoraSigningMethod", defaults.signing);
    setDefault(layout, "agoraDeliveryMethod", defaults.delivery);

    const context = document.implementation.createDocument("", "WebContext", null).documentElement;
    const attributes: Record<string, string> = {
        status: "ok", initialized: "false", draftEdit: "true", journalEdit: "true",
        shortAllowed: String(defaults.shortAllowed), controlEdit: "false", shippingEdit: "false",
        canGenerate: "true", canStart: "true", canCancel: "false",
        createdAt: new Date().toISOString(), temporaryNumber: ""
    };

    for (const [key, value] of Object.entries(attributes))
    {
        context.setAttribute(key, value);
    }

    applyContext(layout, context);
    setVisible(layout, "agoraTasks", false);
}

/** Сохраняет карточку перед операцией только в режиме редактирования. */
export async function saveIfEditing(layout: AgoraLayout): Promise<void>
{
    if (isEditing(layout))
    {
        await layout.saveCard();
    }
}
