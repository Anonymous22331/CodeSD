import * as React from "react";
import type { ActionChoice, DialogResult } from "../Models/AgoraLayout";
import type { $MessageBox } from "@docsvision/webclient/System/$MessageBox";
import type { $RouterNavigation } from "@docsvision/webclient/System/$Router";
import { getCardViewRoute } from "@docsvision/webclient/Platform/ExistingCardRouteHelpers";
import type { IOutgoingUiService } from "./Abstractions/IOutgoingUiService";

/** Использует диалоги и маршрутизатор установленного Web-клиента. */
export class OutgoingUiService implements IOutgoingUiService
{
    /** Получает сервисы интерфейса текущей разметки. */
    public constructor(private readonly services: $MessageBox & $RouterNavigation)
    {
    }

    /** Показывает пользователю понятное сообщение об ошибке. */
    public showError(error: unknown): Promise<void>
    {
        return this.services.messageBox.showError(error instanceof Error ? error.message : String(error));
    }

    /** Показывает результат выполненного действия. */
    public showInfo(message: string): Promise<void>
    {
        return this.services.messageBox.showInfo(message);
    }

    /** Возвращает false при отмене без обработки ее как ошибки операции. */
    public async confirm(message: string): Promise<boolean>
    {
        try
        {
            await this.services.messageBox.showConfirmation(message);
            return true;
        }
        catch
        {
            // В SDK 5.17 отклонение Promise означает отмену подтверждения.
            return false;
        }
    }

    /** Открывает карточку через поддерживаемый помощник маршрута. */
    public openCard(id: string): Promise<void>
    {
        if (!id)
        {
            return Promise.reject(new Error("Сервер не вернул идентификатор карточки."));
        }

        return this.services.routerNavigation.goTo(getCardViewRoute(id));
    }

    /** Показывает доступные действия в стандартном модальном окне платформы. */
    public async chooseAction(title: string, choices: readonly ActionChoice[]): Promise<DialogResult | null>
    {
        if (choices.length === 0)
        {
            return null;
        }
        let selected = 0;
        let comment = "";
        const content = React.createElement("div", { className: "agora-action-dialog" },
            React.createElement("select", {
                "aria-label": "Действие",
                defaultValue: "0",
                onChange: (event: React.ChangeEvent<HTMLSelectElement>) => { selected = Number(event.target.value); }
            }, choices.map((choice, index) => React.createElement("option", { key: index, value: String(index) }, choice.label))),
            React.createElement("textarea", {
                "aria-label": "Комментарий",
                placeholder: "Комментарий (обязателен при отказе)",
                onChange: (event: React.ChangeEvent<HTMLTextAreaElement>) => { comment = event.target.value; }
            }));
        try
        {
            const accepted = await this.services.messageBox.showCustom<boolean>(content, title, [
                { name: "Выполнить", value: true },
                { name: "Отмена", value: false, isCancel: true }
            ]);
            return accepted ? { choice: choices[selected], comment } : null;
        }
        catch
        {
            return null;
        }
    }

    /** Получает текст через showCustom вместо browser prompt. */
    public async input(caption: string, multiline = false): Promise<string | null>
    {
        let value = "";
        const field = React.createElement(multiline ? "textarea" : "input", {
            "aria-label": caption,
            autoFocus: true,
            onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => { value = event.target.value; }
        });
        try
        {
            const accepted = await this.services.messageBox.showCustom<boolean>(field, caption, [
                { name: "Продолжить", value: true },
                { name: "Отмена", value: false, isCancel: true }
            ]);
            return accepted ? value.trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
