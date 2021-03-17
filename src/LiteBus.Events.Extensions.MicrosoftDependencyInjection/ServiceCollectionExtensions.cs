using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LiteBus.Events.Abstractions;
using LiteBus.Registry;
using LiteBus.Registry.Abstractions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBusEvents(this IServiceCollection services,
                                                           Action<ILiteBusEventsBuilder> config)
        {
            var LiteBusBuilder = new LiteBusEventsBuilder();

            config(LiteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LiteBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            services.TryAddTransient<IEventMediator, EventMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}