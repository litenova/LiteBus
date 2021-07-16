using System;
using System.Linq;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#nullable enable

namespace LiteBus.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBus(this IServiceCollection services,
                                                   Action<ILiteBusBuilder> config)
        {
            var liteBusBuilder = new LiteBusBuilder();

            config(liteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(liteBusBuilder.Assemblies.ToArray());
            messageRegistry.Register(liteBusBuilder.Types.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var postHookType in descriptor.PostHandleHookTypes) services.TryAddTransient(postHookType);
                
                foreach (var preHookType in descriptor.PreHandleHookTypes) services.TryAddTransient(preHookType);
            }

            services.TryAddTransient<ICommandMediator, CommandMediator>();
            services.TryAddTransient<IQueryMediator, QueryMediator>();
            services.TryAddTransient<IEventMediator, EventMediator>();
            
            // Todo: add plain message mediator
            // services.TryAddTransient<IMessageMediator, MessageMediator>();
            
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}