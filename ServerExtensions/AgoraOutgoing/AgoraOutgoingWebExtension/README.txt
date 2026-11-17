АГОРА — TypeScript Web-расширение

src/Index.ts — регистрация расширения AgoraOutgoing 1.1.0.
src/EventHandlers — события карточки и команды.
src/Services — вызовы серверного C#-расширения, поля и диалог действий.
src/Models — типы контролов, запросов и решений.
src/Styles — оформление исходящей карточки.
tools — TypeScript-сборка, управляемые проверки и браузерная проверка стенда.

npm ci --ignore-scripts
npm run build
npm run verify

Результат: dist/extension.js и dist/extension.css. JavaScript генерируется
автоматически и в Git не хранится. Редактировать только .ts.

Браузерная проверка: npm run verify:browser.
Указать AGORA_WEB_URL, AGORA_TEST_CREDENTIALS, AGORA_TEST_CARD_ID;
при необходимости AGORA_CHROME_PATH. Личные данные в проект не копировать.

Используется SDK @docsvision/webclient 5.17.0 из npm, без ссылок на внешние
папки рабочего стола. Подробнее — в корневом README.md репозитория.