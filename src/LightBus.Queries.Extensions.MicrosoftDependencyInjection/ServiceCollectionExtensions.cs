using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LightBus.Queries;
using LightBus.Queries.Abstractions;
using LightBus.Registry;
using LightBus.Registry.Abstractions;

namespace LightBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLightBusQueries(this IServiceCollection services,
                                                           Action<ILightBusQueriesBuilder> config)
        {
            var LightBusBuilder = new LightBusQueriesBuilder();

            config(LightBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LightBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            services.TryAddTransient<IQueryMediator, QueryMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);
            
            return services;
        }
    }
}