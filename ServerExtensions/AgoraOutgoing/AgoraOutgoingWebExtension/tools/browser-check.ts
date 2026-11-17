import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";

/** Проверяет загрузку сборки TypeScript и открытие существующей карточки на стенде. */
async function checkBrowser(): Promise<void>
{
    const project = resolve(dirname(fileURLToPath(import.meta.url)), "..");
    const output = resolve(project, "dist");
    const artifact = await readFile(resolve(output, "extension.js"));
    const expectedHash = createHash("sha256").update(artifact).digest("hex");
    const credentialsFile = process.env.AGORA_TEST_CREDENTIALS;
    const baseUrl = (process.env.AGORA_WEB_URL || "http://localhost:8085").replace(/\/$/, "");
    const cardId = process.env.AGORA_TEST_CARD_ID;
    if (!credentialsFile || !cardId) { throw new Error("Set AGORA_TEST_CREDENTIALS and AGORA_TEST_CARD_ID for browser checks."); }
    const credentials = JSON.parse((await readFile(credentialsFile, "utf8")).replace(/^\uFEFF/, "")) as {
        accounts: Array<{ account: string; password: string }>;
    };
    const account = credentials.accounts.find(item => item.account.endsWith("\\Administrator"));

    if (!account)
    {
        throw new Error("Не найдена учётная запись проверки стенда.");
    }

    const browser = await chromium.launch({ executablePath: process.env.AGORA_CHROME_PATH || "C:/Program Files/Google/Chrome/Application/chrome.exe", headless: true });
    const errors: string[] = [];
    const calls: Array<{ method: string; status: number }> = [];
    const scriptLoads: Array<{ url: string; status: number }> = [];

    try
    {
        const page = await browser.newPage({ httpCredentials: { username: account.account, password: account.password }, viewport: { width: 1600, height: 1100 } });
        page.on("pageerror", error => errors.push(error.message));
        page.on("response", response => {
            if (/AgoraOutgoing/i.test(response.url()) && response.request().resourceType() === "script")
            {
                scriptLoads.push({ url: response.url(), status: response.status() });
            }

            if (response.url().includes("/AgoraOutgoing/Execute"))
            {
                const payload = JSON.parse(response.request().postData() || "{}") as { Method?: string };
                calls.push({ method: payload.Method || "", status: response.status() });
            }
        });

        await page.goto(`${baseUrl}/#/CardView/${encodeURIComponent(cardId)}`, { waitUntil: "domcontentloaded", timeout: 120000 });
        await page.locator("[data-control-name='agoraRegistrationGroup']").waitFor({ timeout: 90000 });
        await page.waitForFunction(() => document.querySelectorAll(".agora-outgoing-card").length > 0);
        await page.waitForTimeout(2500);

        const registeredVersion = await page.evaluate(async () => {
            const system = (window as unknown as {
                System?: { import(name: string): Promise<{ extensionManager: { extensions: Array<{ name: string; version: string }> } }> };
            }).System;

            if (!system)
            {
                return null;
            }

            const module = await system.import("@docsvision/webclient/System/ExtensionManager");
            return module.extensionManager.extensions.find(extension => extension.name === "AgoraOutgoing")?.version || null;
        });

        const response = await page.request.get(`${baseUrl}/Content/Modules/AgoraOutgoing/extension.js`);
        const actualHash = createHash("sha256").update(await response.body()).digest("hex");
        const report = { errors, calls, scriptLoads, registeredVersion, artifactStatus: response.status(), expectedHash, actualHash };
        await writeFile(resolve(output, "browser-check.json"), JSON.stringify(report, null, 4));

        if (errors.length > 0 || !response.ok() || actualHash !== expectedHash || registeredVersion !== "1.1.0" || !calls.some(call => call.method === "GetOutgoingWebContext" && call.status === 200))
        {
            throw new Error("Проверка загрузки TypeScript-модуля на стенде не пройдена; см. browser-check.json.");
        }

        console.log("Browser check passed; deployed artifact matches the TypeScript build.");
    }
    finally
    {
        await browser.close();
    }
}

await checkBrowser();
