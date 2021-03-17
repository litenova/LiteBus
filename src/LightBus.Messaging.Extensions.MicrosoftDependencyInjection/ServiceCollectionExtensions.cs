using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LightBus.Messaging.Abstractions;
using LightBus.Registry;
using LightBus.Registry.Abstractions;

namespace LightBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLightBusMessages(this IServiceCollection services,
                                                           Action<ILightBusMessagingBuilder> config)
        {
            var LightBusBuilder = new LightBusMessagingBuilder();

            config(LightBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LightBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            services.TryAddTransient<IMessageMediator, MessageMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}