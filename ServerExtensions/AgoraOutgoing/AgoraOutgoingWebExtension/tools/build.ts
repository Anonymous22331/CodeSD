import { build } from "esbuild";
import { createHash } from "node:crypto";
import { copyFile, mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/** Собирает TypeScript в AMD-модуль для загрузчика Web-клиента 5.5. */
async function buildExtension(): Promise<void>
{
    const project = resolve(dirname(fileURLToPath(import.meta.url)), "..");
    const output = resolve(project, "dist");
    const modules = [
        "@docsvision/webclient/System/ExtensionManager",
        "@docsvision/webclient/System/UrlStore"
    ];

    const result = await build({
        entryPoints: [resolve(project, "src/Index.ts")],
        bundle: true,
        write: false,
        format: "cjs",
        platform: "browser",
        target: "es2017",
        external: modules,
        minify: false,
        charset: "utf8",
        legalComments: "none"
    });

    // Обёртка генерируется сборкой; её не редактируют как исходный JavaScript.
    const prefix = `define(${JSON.stringify(modules)}, function (manager, urls) {
    "use strict";
    var exports = {};
    function require(name) {
        if (name === ${JSON.stringify(modules[0])}) { return manager; }
        if (name === ${JSON.stringify(modules[1])}) { return urls; }
        throw new Error("Unknown external module: " + name);
    }
`;
    const artifact = prefix + result.outputFiles[0].text + "\n});\n";
    await mkdir(output, { recursive: true });
    await writeFile(resolve(output, "extension.js"), artifact, "utf8");
    await copyFile(resolve(project, "src/Styles/extension.css"), resolve(output, "extension.css"));
    await writeFile(resolve(output, "build.json"), JSON.stringify({
        source: "src/Index.ts",
        version: "1.1.0",
        format: "AMD",
        target: "Docsvision Web-client 5.5",
        sha256: createHash("sha256").update(artifact).digest("hex")
    }, null, 4));
    console.log(`Built ${resolve(output, "extension.js")}`);
}

await buildExtension();
