import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { runInNewContext } from "node:vm";

interface Control
{
    params: Record<string, unknown>;
}

interface Extension
{
    name: string;
    version: string;
    globalEventHandlers: Array<Record<string, (...args: unknown[]) => unknown>>;
}

/** Предоставляет XML-атрибуты для проверки обмена с серверным расширением. */
class TestElement
{
    public nodeName = "WebContext";
    public textContent = "";
    private readonly attributes = new Map<string, string>();

    /** Читает значение атрибута тестового ответа. */
    public getAttribute(name: string): string | null
    {
        return this.attributes.get(name) ?? null;
    }

    /** Записывает значение атрибута тестового ответа. */
    public setAttribute(name: string, value: string): void
    {
        this.attributes.set(name, value);
    }
}

/** Проверяет контракт AMD, инициализацию карточки, валидацию и вызовы команд. */
async function verifyExtension(): Promise<void>
{
    const project = resolve(dirname(fileURLToPath(import.meta.url)), "..");
    const output = resolve(project, "dist");
    const artifact = await readFile(resolve(output, "extension.js"), "utf8");
    const requests: Array<Record<string, unknown>> = [];
    const alerts: string[] = [];
    const controls: Record<string, Control> = {};
    const names = ["agoraTheme", "agoraContent", "agoraSheets", "agoraAuthor", "agoraManager", "agoraRegistrationPlace", "agoraJournal", "agoraDocumentType", "agoraSigningMethod", "agoraDeliveryMethod", "agoraSigner", "agoraRecipients", "agoraStart", "agoraShortRoute", "fileList"];

    for (const name of names)
    {
        controls[name] = { params: {} };
    }

    const navigation = { hash: "#/NewCard/test" };
    let extension: Extension | undefined;
    let reloads = 0;
    let saves = 0;
    let managerId = "manager-initial";
    const layout = {
        controls: { ...controls, tryGet: (name: string) => controls[name] },
        cardInfo: { id: "card-test" },
        layoutInfo: { action: 2 },
        saveCard: async () => { saves++; },
        reloadFromServer: async () => { reloads++; }
    };

    runInNewContext(artifact, {
        define: (dependencies: string[], factory: (...modules: unknown[]) => void) => {
            assert.equal(dependencies.length, 2);
            factory({ extensionManager: { registerExtension: (value: Extension) => { extension = value; } } },
                { urlStore: { urlResolver: { resolveUrl: () => "/AgoraOutgoing/Execute" } } });
        },
        window: { location: navigation, alert: (message: string) => alerts.push(message), confirm: () => false },
        document: { implementation: { createDocument: () => ({ documentElement: new TestElement() }) } },
        DOMParser: class {
            /** Разбирает атрибуты управляемого тестового XML-ответа. */
            public parseFromString(xml: string): { documentElement: TestElement }
            {
                const root = new TestElement();
                for (const attribute of xml.matchAll(/(\w+)="([^"]*)"/g))
                {
                    root.setAttribute(attribute[1], attribute[2]);
                }
                return { documentElement: root };
            }
        },
        fetch: async (_url: string, options: { body: string }) => {
            const payload = JSON.parse(options.body) as Record<string, unknown>;
            requests.push(payload);
            const defaults = {
                author: { id: "author-test" }, unit: { id: "unit-test" },
                manager: { id: managerId }, journal: { id: payload.Journal === "journal-manual" ? "journal-manual" : "journal-default" },
                documentType: { id: "type-test" }, signing: { id: "signature-test" }, shortAllowed: true
            };
            return {
                ok: true,
                json: async () => ({ ok: true, defaults, xml: "<WebContext status=\"ok\" draftEdit=\"true\" canNumber=\"true\"/>" })
            };
        }
    });

    assert.ok(extension);
    assert.equal(extension.name, "AgoraOutgoing");
    const handlers = Object.assign({}, ...extension.globalEventHandlers);
    const expected = ["AgoraOpened", "AgoraSaving", "AgoraSaved", "AgoraGenerate", "AgoraStart", "AgoraSend", "AgoraCancel", "AgoraSave", "AgoraTasks", "AgoraShortChanged", "AgoraJournalChanged"];
    assert.deepEqual(Object.keys(handlers).sort(), expected.sort());

    await handlers.AgoraOpened(layout);
    assert.equal(requests[0].Method, "GetOutgoingCreationDefaults");
    assert.equal((controls.agoraAuthor.params.value as { id: string }).id, "author-test");
    assert.equal(controls.agoraAuthor.params.disabled, true);
    assert.equal(controls.agoraRegistrationPlace.params.disabled, true);

    controls.agoraJournal.params.value = { id: "journal-manual" };
    managerId = "manager-other-journal";
    await handlers.AgoraJournalChanged(layout);
    assert.equal((controls.agoraJournal.params.value as { id: string }).id, "journal-manual");
    assert.equal((controls.agoraManager.params.value as { id: string }).id, managerId);

    controls.agoraTheme.params.value = "Тестовая тема";
    controls.agoraContent.params.value = "Тестовое содержание";
    controls.agoraSigner.params.value = [{ id: "signer-test" }];
    controls.agoraRecipients.params.rows = [{}];
    controls.fileList.params.files = [{ data: { isMain: true } }];
    controls.agoraSheets.params.value = -1.5;
    let cancelled = false;
    handlers.AgoraSaving(layout, { cancel: () => { cancelled = true; } });
    assert.equal(cancelled, true);
    assert.ok(alerts.some(message => message.includes("Количество листов")));

    controls.agoraSheets.params.value = 2;
    navigation.hash = "#/CardView/card-test";
    const saving: { cancel: () => void; deferred?: Promise<unknown> } = { cancel: () => { throw new Error("Unexpected validation cancellation"); } };
    handlers.AgoraSaving(layout, saving);
    await saving.deferred;
    assert.equal(requests.at(-1)?.Method, "ValidateOutgoingJournalChange");
    assert.equal(requests.at(-1)?.Journal, "journal-manual");

    controls.agoraShortRoute.params.value = true;
    handlers.AgoraShortChanged(layout);
    assert.equal(controls.agoraStart.params.text, "Зарегистрировать");
    await handlers.AgoraGenerate(layout);
    assert.equal(requests.at(-1)?.Method, "GenerateMainLetter");
    assert.equal(saves, 1);
    assert.equal(reloads, 1);

    await handlers.AgoraSaved(layout);
    assert.equal(requests.at(-1)?.Method, "AssignDraftNumber");
    assert.equal(reloads, 2);
    await writeFile(resolve(output, "verification.json"), JSON.stringify({
        handlers: expected,
        amdRegistration: true,
        defaultsAndReadOnlyFields: true,
        journalRefresh: true,
        numericValidation: true,
        serverJournalValidation: true,
        generateAndNumbering: true
    }, null, 4));
    console.log("TypeScript client checks passed.");
}

await verifyExtension();
