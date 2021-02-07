using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paykan.Commands;
using Paykan.Commands.Abstraction;
using Paykan.Events;
using Paykan.Events.Abstraction;
using Paykan.Messaging;
using Paykan.Messaging.Abstractions;
using Paykan.Queries;
using Paykan.Queries.Abstraction;
using Paykan.Registry;
using Paykan.Registry.Abstractions;
#nullable enable

namespace Paykan.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaykan(this IServiceCollection services,
                                                   Action<IPaykanBuilder> config)
        {
            var paykanBuilder = new PaykanBuilder();

            config(paykanBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(paykanBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.AddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            var commandMediatorBuilder = new CommandMediatorBuilder();
            var queryMediatorBuilder = new QueryMediatorBuilder();
            var eventMediatorBuilder = new EventMediatorBuilder();
            var messageMediatorBuilder = new MessageMediatorBuilder();

            services.AddSingleton<ICommandMediator>(f => commandMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IQueryMediator>(f => queryMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IEventMediator>(f => eventMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IMessageMediator>(f => messageMediatorBuilder.Build(f, messageRegistry));
            
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}