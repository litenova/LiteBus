using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
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
            services.TryAddSingleton(MessageRegistryAccessor.MessageRegistry);
            services.TryAddTransient<IMessageMediator, MessageMediator>();
            
            var liteBusBuilder = new LiteBusBuilder(services, MessageRegistryAccessor.MessageRegistry);

            liteBusBuilderAction(liteBusBuilder);
            
            liteBusBuilder.Build();

            return services;
        }
    }
}