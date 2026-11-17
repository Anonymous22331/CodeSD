using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Autofac;
using DocsVision.Platform.WebClient;
using DocsVision.WebClient.Extensibility;

[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
namespace Agora.Outgoing.Web
{
    /// <summary>Подключает сервисы и HTTP-адаптер исходящих писем.</summary>
    public sealed class AgoraWebExtension : WebClientExtension
    {
        /// <summary>Создает точку входа серверного расширения.</summary>
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
                return new Version(1, 1, 0, 0);
            }
        }

        /// <summary>Регистрирует сервисы без состояния пользовательской сессии.</summary>
        public override void InitializeContainer(ContainerBuilder builder)
        {
            builder.RegisterType<OutgoingCreationService>().As<IOutgoingCreationService>().SingleInstance();
            builder.RegisterType<OutgoingActionService>().As<IOutgoingActionService>().SingleInstance();
            builder.RegisterType<OutgoingSetupService>().As<IOutgoingSetupService>().InstancePerDependency();
        }

        /// <summary>Сохраняет маршруты MVC установленного Web-клиента 5.5.</summary>
        protected override Dictionary<Type, Func<IController>> GetControllerActivators(IServiceProvider services)
        {
            return new Dictionary<Type, Func<IController>>
            {
                {
                    typeof(AgoraOutgoingController),
                    () => new AgoraOutgoingController(
                        (ICurrentObjectContextProvider)services.GetService(typeof(ICurrentObjectContextProvider)),
                        (IOutgoingCreationService)services.GetService(typeof(IOutgoingCreationService)),
                        (IOutgoingActionService)services.GetService(typeof(IOutgoingActionService)),
                        (IOutgoingSetupService)services.GetService(typeof(IOutgoingSetupService)))
                }
            };
        }
    }
}
