using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LightBus.Commands.Abstractions;
using LightBus.Registry;
using LightBus.Registry.Abstractions;

namespace LightBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLightBusCommands(this IServiceCollection services,
                                                   Action<ILightBusCommandsBuilder> config)
        {
            var LightBusBuilder = new LightBusCommandsBuilder();

            config(LightBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LightBusBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            services.TryAddTransient<ICommandMediator, CommandMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}