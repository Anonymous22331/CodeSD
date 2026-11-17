import type { ActionChoice, DialogResult } from "../../Models/AgoraLayout";
import { serviceName } from "@docsvision/webclient/System/ServiceUtils";

/** Определяет платформенные диалоги и навигацию исходящего письма. */
export interface IOutgoingUiService
{
    /** Показывает ошибку операции. */
    showError(error: unknown): Promise<void>;
    /** Показывает информационное сообщение. */
    showInfo(message: string): Promise<void>;
    /** Запрашивает подтверждение и различает отмену. */
    confirm(message: string): Promise<boolean>;
    /** Открывает карточку штатным маршрутом. */
    openCard(id: string): Promise<void>;
    /** Выбирает доступное действие и комментарий в платформенном диалоге. */
    chooseAction(title: string, choices: readonly ActionChoice[]): Promise<DialogResult | null>;
    /** Запрашивает строку в платформенном модальном окне. */
    input(caption: string, multiline?: boolean): Promise<string | null>;
}

export type $OutgoingUi = { outgoingUi: IOutgoingUiService };
export const $OutgoingUi = serviceName((services?: $OutgoingUi) => services!.outgoingUi);
