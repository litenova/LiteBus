using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paykan.Abstractions;
using Paykan.Builders;

namespace Paykan.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaykan(this IServiceCollection services,
                                                   Action<IMessageRegistryBuilder> configureBuilder)
        {
            var messageRegistryBuilder = new MessageRegistryBuilder();

            configureBuilder(messageRegistryBuilder);

            var messageRegistry = messageRegistryBuilder.Build();

            foreach (var descriptor in messageRegistry.Values)
            {
                foreach (var handlerType in descriptor.HandlerTypes)
                {
                    services.AddTransient(handlerType);
                }

                foreach (var hookType in descriptor.PostHandleHookTypes)
                {
                    services.TryAddTransient(hookType);
                }
            }

            var commandMediatorBuilder = new CommandMediatorBuilder();
            var queryMediatorBuilder = new QueryMediatorBuilder();
            var eventMediatorBuilder = new EventMediatorBuilder();
            var messageMediatorBuilder = new MessageMediatorBuilder();

            services.AddSingleton<ICommandMediator>(f => commandMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IQueryMediator>(f => queryMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IEventMediator>(f => eventMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IMessageMediator>(f => messageMediatorBuilder.Build(f, messageRegistry));

            return services;
        }
    }
}