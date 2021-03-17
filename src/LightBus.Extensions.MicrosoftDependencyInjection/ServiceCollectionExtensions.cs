using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LightBus.Commands;
using LightBus.Commands.Abstractions;
using LightBus.Events;
using LightBus.Events.Abstractions;
using LightBus.Messaging;
using LightBus.Messaging.Abstractions;
using LightBus.Queries;
using LightBus.Queries.Abstractions;
using LightBus.Registry;
using LightBus.Registry.Abstractions;
#nullable enable

namespace LightBus.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLightBus(this IServiceCollection services,
                                                   Action<ILightBusBuilder> config)
        {
            var LightBusBuilder = new LightBusBuilder();

            config(LightBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LightBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            services.TryAddTransient<ICommandMediator, CommandMediator>();
            services.TryAddTransient<IQueryMediator, QueryMediator>();
            services.TryAddTransient<IEventMediator, EventMediator>();
            services.TryAddTransient<IMessageMediator, MessageMediator>();
            
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}