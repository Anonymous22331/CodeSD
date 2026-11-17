# АГОРА — исходящие письма, Docsvision 5.5

Исходники работающего решения: серверные операции, Web-клиент на TypeScript, сценарий Windows-клиента, обработчики бизнес-процессов и разметки.

Структура Web-расширения следует [официальным примерам Docsvision](https://github.com/Docsvision/WebClient-Samples): `ServerExtensions/<решение>/<ServerExtension|WebExtension>`, отдельные контроллеры, модели, сервисы и обработчики событий. API сохранён для **Docsvision 5.5**, а не перенесён на .NET 6 из текущего master примеров.

## Структура

```text
AgoraOutgoing.sln
Directory.Build.props / Directory.Build.targets
ServerExtensions/AgoraOutgoing/
  AgoraOutgoingServerExtension/    C#-расширение Web-клиента
    Controllers/
    Models/
  AgoraOutgoingWebExtension/       TypeScript, package-lock.json, сборка
    src/Index.ts
    src/EventHandlers/
    src/Services/
    src/Models/
    src/Styles/
    tools/
StorageServer/AgoraOutgoingServer/ Серверная бизнес-логика C#
  Approval/ Documents/ Numbering/ Registration/ Security/ Sending/
Workflow/
  AgoraOutgoingWorkflow/           Обработчики, права, регистрация, архив, уведомления
  Scripts/                        Код штатных программных узлов БП
ClientScripts/CSharp/              Сценарий исходящей карточки Windows-клиента
RuntimeTools/                      C#-инфраструктура: запуск, SMTP, CRL, PDF, поиск
Tests/                            Проверка тестовой почты и защиты подписи
Install/WebLayouts/AgoraOutgoing/  Четыре разметки и условия выбора с платформы
Install/Configuration/             Пример идентификаторов объектов стенда
```

## Сборка C#

Нужны Windows, .NET SDK / MSBuild с поддержкой SDK-проектов и .NET Framework 4.7.2. Проверено с .NET SDK 10.0.201.

Библиотеки **установленного Docsvision 5.5** предоставляются локально: они не распространяются в Git. Соберите их в отдельную папку и укажите её:

```console
dotnet build AgoraOutgoing.sln -p:DocsvisionSdkPath="C:\Sdk\Docsvision55"
```

Список файлов проверенного SDK приведён в `Install/DocsvisionSdkReferences.txt`. Источники библиотек: каталоги Client, Client/BaseObjects, Platform/5.5, WebClient/5.5/Site/bin и GAC установленного стенда. Папка `.sdk` в корне также поддерживается и исключена из Git.

Сборки имеют те же имена, что на стенде: `Agora.Outgoing.Server.dll`, `Agora.Outgoing.Web.dll`, `Agora.Outgoing.Workflow.dll`, `Agora.Outgoing.Runner.exe`, `AgoraPdfOverlay.dll` и вспомогательные компоненты. Библиотека PDFsharp восстанавливается из NuGet.

Для подписания серверной сборки используйте **внешний** существующий ключ:

```console
dotnet build StorageServer/AgoraOutgoingServer/AgoraOutgoingServer.csproj -p:DocsvisionSdkPath="C:\Sdk\Docsvision55" -p:AgoraSigningKey="C:\Private\Agora.Server.snk"
```

Без этого параметра проект собирается для проверки исходников, но такую сборку нельзя автоматически подставлять вместо зарегистрированной подписанной сборки.

## Сборка Web-клиента

```console
cd ServerExtensions/AgoraOutgoing/AgoraOutgoingWebExtension
npm ci --ignore-scripts
npm run build
npm run verify
```

Проверено на Node.js 18. Используются TypeScript 5.7.3 и `@docsvision/webclient` **5.17.0**, соответствующий Web-клиенту 17 на платформе 5.5. Версии инструментов и зависимостей зафиксированы в lock-файле.

Результат: `dist/extension.js` и `dist/extension.css`. JavaScript **генерируется** для браузера и не хранится как исходник. Редактировать нужно файлы `.ts`. Артефакты и `node_modules` исключены из Git.

Сборку размещают в каталоге `Site/Content/Modules/AgoraOutgoing` установленного Web-клиента. Имена всех 11 обработчиков сохранены и совпадают с привязками импортированных разметок.

Необязательная браузерная проверка существующей карточки:

```console
set AGORA_WEB_URL=http://localhost:8085
set AGORA_TEST_CREDENTIALS=C:\Private\test-accounts.json
set AGORA_TEST_CARD_ID=<идентификатор-существующего-письма>
npm run verify:browser
```

Формат внешнего файла: `{"accounts":[{"account":"MACHINE\\Administrator","password":"..."}]}`. Реальные файлы учётных данных не помещаются в проект. Путь к Chrome при необходимости задаётся через `AGORA_CHROME_PATH`.

## Бизнес-процессы и клиентский сценарий

Узлы из `Workflow/Scripts` компилируются **по отдельности самой платформой**, поэтому не включены в DLL как два одинаковых класса `DVScript`. В узле штатным списком сборок подключается `Agora.Outgoing.Workflow.dll`:

```csharp
ProcessHandlers.RegisterAndNotify();
ProcessHandlers.ArchiveSentDocuments();
```

В узлах нет reflection, PowerShell или вызова PS1. `ProcessMigration` обновляет два шаблона и два экземпляра через API Workflow, проверяет компиляцию и сохраняет данные для отката. `ClientScriptMigration` компилирует и сохраняет клиентский сценарий через `IScriptingService`.

Обработчики ожидают установленный стенд и конфигурацию в `C:\AGORA-OUT`. Файл `Install/Configuration/processes.example.json` содержит идентификаторы существующих объектов этого решения. Исходники и разметки **не заменяют резервную копию базы**: создание всей метасхемы чистой базы этим репозиторием не выполняется.

## Правила разработки

- C#: четыре пробела, скобки на отдельных строках, краткий русский XML `summary` у методов и конструкторов.
- TypeScript: строгая проверка типов; прикладной код разделён на модели, сервисы и события.
- Использовать API установленной версии Docsvision и штатные ссылки на сборки в БП.
- SDK, бинарные результаты, сертификаты, закрытые ключи, пароли, журналы и резервные копии в Git не добавлять.

Криптографические проверки подписи сохраняются. Тестовый SMTP принимает только домен `agora.test`; реальная внешняя почта не настраивается автоматически.
