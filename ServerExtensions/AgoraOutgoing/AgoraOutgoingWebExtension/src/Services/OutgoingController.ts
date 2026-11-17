import { urlStore } from "@docsvision/webclient/System/UrlStore";
import type { AgoraLayout, CreationDefaults } from "../Models/AgoraLayout";

interface ApiResponse
{
    ok: boolean;
    message?: string;
    xml?: string;
    defaults?: CreationDefaults;
}

/** Отправляет запрос серверному расширению с текущей Windows-сессией. */
async function request(payload: Readonly<Record<string, unknown>>): Promise<ApiResponse>
{
    const response = await fetch(urlStore.urlResolver.resolveUrl("Execute", "AgoraOutgoing"), {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json", "X-Agora-Web": "1" },
        body: JSON.stringify(payload)
    });
    const data = await response.json() as ApiResponse;

    if (!response.ok || !data.ok)
    {
        throw new Error(data.message || "Не удалось выполнить действие.");
    }

    return data;
}

/** Вызывает операцию серверного расширения и проверяет XML-результат. */
export async function callOperation(layout: AgoraLayout, method: string, parameters: Readonly<Record<string, unknown>> = {}): Promise<Element>
{
    const data = await request({ CardId: layout.cardInfo.id, Method: method, ...parameters });

    if (!data.xml)
    {
        throw new Error("Сервер не вернул результат операции.");
    }

    const root = new DOMParser().parseFromString(data.xml, "text/xml").documentElement;

    if (root.nodeName === "parsererror" || root.getAttribute("status") === "error" || root.getAttribute("valid") === "false")
    {
        throw new Error(root.textContent || "Операция отклонена.");
    }

    return root;
}

/** Запрашивает значения новой карточки до её первого сохранения. */
export async function getCreationDefaults(journal: string | undefined): Promise<CreationDefaults>
{
    const data = await request({ Method: "GetOutgoingCreationDefaults", Journal: journal || "00000000-0000-0000-0000-000000000000" });

    if (!data.defaults)
    {
        throw new Error("Не удалось заполнить новую карточку.");
    }

    return data.defaults;
}
