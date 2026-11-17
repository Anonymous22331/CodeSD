using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using DocsVision.Platform.WebClient;
using DocsVision.WebClient.Extensibility;
using DocsVision.Platform.ObjectManager;
using DocsVision.Platform.ObjectModel;
using DocsVision.BackOffice.ObjectModel;
using DocsVision.BackOffice.ObjectModel.Services;
using DocsVision.WebClientLibrary.ObjectModel.SolutionsDirectory.Export;

[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]
namespace Agora.Outgoing.Web
{
    public sealed class AgoraWebExtension : WebClientExtension
    {
        /// <summary>
        /// Создаёт серверное расширение Web-клиента АГОРА.
        /// </summary>
        public AgoraWebExtension(IServiceProvider services) : base(services)
        {
        }

        public override string ExtensionName
        {
            get
            {
                return "AgoraOutgoingWeb";
            }
        }

        public override Version ExtensionVersion
        {
            get
            {
                return new Version(1, 0, 1, 0);
            }
        }

        /// <summary>
        /// Регистрирует контроллеры серверного расширения.
        /// </summary>
        protected override Dictionary<Type, Func<IController>> GetControllerActivators(IServiceProvider services)
        {
            return new Dictionary<Type, Func<IController>>
            {
                {
                    typeof(AgoraOutgoingController),
                    () => new AgoraOutgoingController((ICurrentObjectContextProvider)services.GetService(typeof(ICurrentObjectContextProvider)))
                }
            };
        }
    }
}
