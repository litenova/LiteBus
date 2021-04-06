using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LiteBus.Registry;
using LiteBus.Registry.Abstractions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBusMessages(this IServiceCollection services,
                                                           Action<ILiteBusMessagingBuilder> config)
        {
            var liteBusBuilder = new LiteBusMessagingBuilder();

            config(liteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(liteBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            // Todo: add plain message mediator
            // services.TryAddTransient<IMessageMediator, MessageMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}