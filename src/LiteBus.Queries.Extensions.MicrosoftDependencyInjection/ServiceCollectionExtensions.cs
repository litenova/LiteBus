using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LiteBus.Queries.Abstractions;
using LiteBus.Registry;
using LiteBus.Registry.Abstractions;

namespace LiteBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBusQueries(this IServiceCollection services,
                                                           Action<ILiteBusQueriesBuilder> config)
        {
            var liteBusBuilder = new LiteBusQueriesBuilder();

            config(liteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(liteBusBuilder.Assemblies.ToArray());

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