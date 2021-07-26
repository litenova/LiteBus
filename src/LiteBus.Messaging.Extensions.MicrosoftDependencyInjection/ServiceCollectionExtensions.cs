using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection.Abstractions;
using LiteBus.Messaging.Internal.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBus(this IServiceCollection services, 
                                                    Action<ILiteBusBuilder> liteBusBuilderAction)
        {
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);
            
            var liteBusBuilder = new LiteBusBuilder(services, MessageRegistryAccessor.MessageRegistry);

            liteBusBuilderAction(liteBusBuilder);

            return services;
        }
    }
}