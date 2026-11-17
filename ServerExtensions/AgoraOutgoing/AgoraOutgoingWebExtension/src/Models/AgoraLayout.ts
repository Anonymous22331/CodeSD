import type { ICancelableEventArgs } from "@docsvision/webclient/System/ICancelableEventArgs";
import type { LayoutControl } from "@docsvision/webclient/System/BaseControl";
import type { Layout } from "@docsvision/webclient/System/Layout";

/** Значение справочного поля исходящей карточки. */
export interface DirectoryValue
{
    id?: string;
    itemId?: string;
    displayName?: string;
}

/** Параметры используемых стандартных контролов Web-клиента. */
export type AgoraControl = LayoutControl & {
    params: {
        value?: unknown;
        editMode?: number;
        disabled?: boolean;
        visibility?: boolean;
        text?: string;
        multipleSelection?: boolean;
        rows?: readonly unknown[];
        files?: ReadonlyArray<{ data?: { isMain?: boolean } }>;
    };
};

/** Разметка платформы с локальным состоянием обработчиков АГОРА. */
export type AgoraLayout = Pick<Layout, "getService" | "controls" | "cardInfo" | "layoutInfo" | "saveCard" | "reloadFromServer"> & {
    __agoraContext?: Element;
    __agoraPersisted?: boolean;
    __agoraNumbering?: boolean;
    __agoraExecutingAction?: boolean;
    __agoraSavePurpose?: string | null;
    __agoraJournalChanged?: boolean;
};

export type EventSender = AgoraLayout | { layout: AgoraLayout };

/** Аргументы отменяемого события сохранения платформы. */
export type SavingArguments = ICancelableEventArgs<unknown>;

/** Начальные значения новой исходящей карточки. */
export interface CreationDefaults
{
    author?: DirectoryValue;
    unit?: DirectoryValue;
    journal?: DirectoryValue;
    manager?: DirectoryValue;
    documentType?: DirectoryValue;
    signing?: DirectoryValue;
    delivery?: DirectoryValue;
    shortAllowed: boolean;
}

export type ActionChoice =
    | { kind: "open"; label: string; cardId: string }
    | { kind: "decision"; label: string; taskId: string; semantic: number; save: boolean; required: boolean }
    | { kind: "operation"; label: string; operation: number };

export interface DialogResult
{
    choice: ActionChoice;
    comment: string;
}
