using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paykan.Events.Extensions.MicrosoftDependencyInjection;
using Paykan.Registry;
using Paykan.Registry.Abstractions;

namespace Paykan.Events.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaykanEvents(this IServiceCollection services,
                                                           Action<IPaykanEventsBuilder> config)
        {
            var paykanBuilder = new PaykanEventsBuilder();

            config(paykanBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(paykanBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.AddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            var eventMediatorBuilder = new EventMediatorBuilder();

            services.AddSingleton(f => eventMediatorBuilder.Build(f, messageRegistry));
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}