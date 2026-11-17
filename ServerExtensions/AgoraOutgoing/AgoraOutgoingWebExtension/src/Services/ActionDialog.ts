import type { ActionChoice, DialogResult } from "../Models/AgoraLayout";

/** Показывает список доступных действий и возвращает выбранное действие с комментарием. */
export function showActionDialog(title: string, choices: readonly ActionChoice[]): Promise<DialogResult | null>
{
    return new Promise(resolve => {
        const dialog = document.createElement("dialog");
        dialog.className = "agora-action-dialog";
        const heading = document.createElement("h3");
        heading.textContent = title;
        const select = document.createElement("select");

        for (const [index, choice] of choices.entries())
        {
            const option = document.createElement("option");
            option.value = String(index);
            option.textContent = choice.label;
            select.appendChild(option);
        }

        const comment = document.createElement("textarea");
        comment.placeholder = "Комментарий (обязателен при отказе)";
        const submit = document.createElement("button");
        submit.textContent = "Выполнить";
        const cancel = document.createElement("button");
        cancel.textContent = "Отмена";

        /** Закрывает диалог и возвращает результат обработчику команды. */
        function close(result: DialogResult | null): void
        {
            dialog.close();
            dialog.remove();
            resolve(result);
        }

        submit.onclick = () => close({ choice: choices[Number(select.value)], comment: comment.value });
        cancel.onclick = () => close(null);
        dialog.addEventListener("cancel", event => {
            event.preventDefault();
            close(null);
        });
        dialog.append(heading, select, comment, submit, cancel);
        document.body.appendChild(dialog);
        dialog.showModal();
    });
}
