import { serviceName } from "@docsvision/webclient/System/ServiceUtils";
import type { CreationDefaults } from "../../Models/AgoraLayout";

/** Контракт операций исходящего письма в текущей разметке. */
export interface IOutgoingService
{
    /** Выполняет платформенную операцию и проверяет ее XML-результат. */
    execute(cardId: string, method: string, parameters?: Readonly<Record<string, unknown>>): Promise<Element>;
    /** Получает начальные значения новой карточки. */
    getCreationDefaults(journal?: string): Promise<CreationDefaults>;
}

export type $Outgoing = { outgoing: IOutgoingService };
export const $Outgoing = serviceName((services?: $Outgoing) => services!.outgoing);
