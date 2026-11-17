import type { $RequestManager } from "@docsvision/webclient/System/$RequestManager";
import type { $UrlResolver } from "@docsvision/webclient/System/$UrlResolver";
import type { CreationDefaults } from "../Models/AgoraLayout";
import type { IOutgoingService } from "./Abstractions/IOutgoingService";

/** Обращается к серверу через сервисы текущей разметки. */
export class OutgoingService implements IOutgoingService
{
    /** Получает зависимости без привязки к глобальной карточке. */
    public constructor(private readonly services: $RequestManager & $UrlResolver)
    {
    }

    /** Выполняет запрос со штатной обработкой ошибок и timestamp. */
    private request<T>(payload: Readonly<Record<string, unknown>>): Promise<T>
    {
        const url = this.services.urlResolver.resolveUrl("Execute", "AgoraOutgoing");
        return this.services.requestManager.post<T>(url, JSON.stringify(payload), {
            contentType: "application/json",
            customHeaders: [{ name: "X-Agora-Web", value: "1" }],
            disableDialogsOnErrors: true
        });
    }

    /** Проверяет XML и возвращает результат платформенной операции. */
    public async execute(cardId: string, method: string, parameters: Readonly<Record<string, unknown>> = {}): Promise<Element>
    {
        const data = await this.request<{ xml: string }>({ ...parameters, CardId: cardId, Method: method });
        if (!data.xml)
        {
            throw new Error("Сервер не вернул результат операции.");
        }

        const document = new DOMParser().parseFromString(data.xml, "text/xml");
        const root = document.documentElement;
        if (document.getElementsByTagName("parsererror").length > 0 || root.getAttribute("status") === "error" || root.getAttribute("valid") === "false")
        {
            throw new Error(root.textContent || "Операция отклонена.");
        }

        return root;
    }

    /** Загружает значения полей до первого сохранения карточки. */
    public getCreationDefaults(journal?: string): Promise<CreationDefaults>
    {
        return this.request<CreationDefaults>({ Method: "GetOutgoingCreationDefaults", Journal: journal || "00000000-0000-0000-0000-000000000000" });
    }
}
