using System;
using System.ComponentModel.Design;
using DocsVision.BackOffice.ObjectModel.Mapping;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.Platform.Data.Metadata;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.Platform.ObjectModel.Mapping;
using DocsVision.Platform.ObjectModel.Persistence;
using DocsVision.Platform.SystemCards.ObjectModel.Mapping;
using DocsVision.Platform.SystemCards.ObjectModel.Services;
namespace Agora.Outgoing.Deployment {
 public sealed class DVSessionProvider : ISessionProvider { public UserSession Session { get; set; } }
 public static class ContextConnection {
  public static UserSession Session { get; private set; }
  public static ObjectContext Context { get; private set; }
  public static ObjectContext Open(string baseName) {
   var manager = SessionManager.CreateInstance();
   manager.Connect("http://localhost/DocsVision/StorageServer/StorageServerService.asmx", baseName);
   return Initialize(manager.CreateSession());
  }
  public static ObjectContext OpenAs(string baseName, string userName, string password) {
   var manager = SessionManager.CreateInstance();
   manager.Connect("http://localhost/DocsVision/StorageServer/StorageServerService.asmx", baseName, userName, password);
   return Initialize(manager.CreateSession());
  }
  static ObjectContext Initialize(UserSession session) {
   Session = session;
   var container = new ServiceContainer();
   container.AddService(typeof(UserSession), Session);
   Context = new ObjectContext(container);
   var mappers = Context.GetService<IObjectMapperFactoryRegistry>();
   mappers.RegisterFactory(typeof(SystemCardsMapperFactory));
   mappers.RegisterFactory(typeof(BackOfficeMapperFactory));
   var services = Context.GetService<IServiceFactoryRegistry>();
   services.RegisterFactory(typeof(SystemCardsServiceFactory));
   services.RegisterFactory(typeof(BackOfficeServiceFactory));
   Context.AddService<IPersistentStore>(DocsVisionObjectFactory.CreatePersistentStore(new DVSessionProvider { Session = Session }, null));
   var provider = DocsVisionObjectFactory.CreateMetadataProvider(Session);
   Context.AddService<IMetadataManager>(DocsVisionObjectFactory.CreateMetadataManager(provider, Session));
   Context.AddService<IMetadataProvider>(provider);
   return Context;
  }
  public static void MoveState(DocsVision.BackOffice.ObjectModel.BaseCard card, DocsVision.BackOffice.ObjectModel.StatesStateMachineBranch branch) {
   Context.GetService<IStateService>().ChangeState(card, branch);
  }
  public static void Close() {
   if (Session != null) { Session.Close(); Session = null; }
   Context = null;
  }
 }
}


