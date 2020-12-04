using System;
using BasicBus.Abstractions;
using BasicBus.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace BasicBus.Extensions.Microsoft.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBasicBus(this IServiceCollection services,
                                                     Action<IMessageRegistryBuilder> configureBuilder)
        {
            var messageRegistryBuilder = new MessageRegistryBuilder();

            configureBuilder(messageRegistryBuilder);

            var messageRegistry = messageRegistryBuilder.Build();

            foreach (var descriptor in messageRegistry.Values)
            {
                services.AddTransient(descriptor.HandlerType);
            }

            var commandMediatorBuilder = new CommandMediatorBuilder();
            var queryMediatorBuilder = new QueryMediatorBuilder();

            services.AddSingleton<ICommandMediator>(f => commandMediatorBuilder.Build(f, messageRegistry));
            services.AddSingleton<IQueryMediator>(f => queryMediatorBuilder.Build(f, messageRegistry));

            return services;
        }
    }
}