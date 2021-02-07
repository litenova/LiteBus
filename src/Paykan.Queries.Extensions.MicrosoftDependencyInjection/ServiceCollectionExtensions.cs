using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paykan.Queries.Extensions.MicrosoftDependencyInjection;
using Paykan.Registry;
using Paykan.Registry.Abstractions;

namespace Paykan.Queries.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaykanQueries(this IServiceCollection services,
                                                           Action<IPaykanQueriesBuilder> config)
        {
            var paykanBuilder = new PaykanQueriesBuilder();

            config(paykanBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(paykanBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.AddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            var queryMediatorBuilder = new QueryMediatorBuilder();

            services.AddSingleton(f => queryMediatorBuilder.Build(f, messageRegistry));
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);
            
            return services;
        }
    }
}