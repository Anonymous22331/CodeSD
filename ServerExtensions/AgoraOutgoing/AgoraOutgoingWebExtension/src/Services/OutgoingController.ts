import type { AgoraLayout, CreationDefaults } from "../Models/AgoraLayout";
import { $Outgoing } from "./Abstractions/IOutgoingService";

/** Вызывает сервис текущей карточки из обработчика платформы. */
export function callOperation(layout: AgoraLayout, method: string, parameters: Readonly<Record<string, unknown>> = {}): Promise<Element>
{
    return layout.getService($Outgoing).execute(layout.cardInfo.id, method, parameters);
}

/** Получает начальные значения через сервис текущей разметки. */
export function getCreationDefaults(layout: AgoraLayout, journal?: string): Promise<CreationDefaults>
{
    return layout.getService($Outgoing).getCreationDefaults(journal);
}
