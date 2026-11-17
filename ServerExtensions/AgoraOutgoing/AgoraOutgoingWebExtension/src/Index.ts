import { extensionManager } from "@docsvision/webclient/System/ExtensionManager";
import { Service } from "@docsvision/webclient/System/Service";
import type { $RequestManager } from "@docsvision/webclient/System/$RequestManager";
import type { $UrlResolver } from "@docsvision/webclient/System/$UrlResolver";
import type { $MessageBox } from "@docsvision/webclient/System/$MessageBox";
import type { $RouterNavigation } from "@docsvision/webclient/System/$Router";
import { $Outgoing } from "./Services/Abstractions/IOutgoingService";
import { $OutgoingUi } from "./Services/Abstractions/IOutgoingUiService";
import { OutgoingService } from "./Services/OutgoingService";
import { OutgoingUiService } from "./Services/OutgoingUiService";
import * as CardEvents from "./EventHandlers/CardEvents";
import * as DocumentActions from "./EventHandlers/DocumentActions";
import * as TaskActions from "./EventHandlers/TaskActions";

// Имена событий совпадают с привязками в конструкторе Web-разметок.
extensionManager.registerExtension({
    name: "AgoraOutgoing",
    version: "1.2.0",
    layoutServices: [
        Service.fromFactory($Outgoing, (services: $RequestManager & $UrlResolver) => new OutgoingService(services)),
        Service.fromFactory($OutgoingUi, (services: $MessageBox & $RouterNavigation) => new OutgoingUiService(services))
    ],
    globalEventHandlers: [CardEvents, DocumentActions, TaskActions]
});
