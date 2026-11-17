import { extensionManager } from "@docsvision/webclient/System/ExtensionManager";
import * as CardEvents from "./EventHandlers/CardEvents";
import * as DocumentActions from "./EventHandlers/DocumentActions";
import * as TaskActions from "./EventHandlers/TaskActions";

// Имена событий совпадают с привязками в конструкторе Web-разметок.
extensionManager.registerExtension({
    name: "AgoraOutgoing",
    version: "1.1.0",
    globalEventHandlers: [CardEvents, DocumentActions, TaskActions]
});
