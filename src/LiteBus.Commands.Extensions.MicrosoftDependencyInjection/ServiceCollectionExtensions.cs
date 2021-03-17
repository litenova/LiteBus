using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LiteBus.Commands.Abstractions;
using LiteBus.Registry;
using LiteBus.Registry.Abstractions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLiteBusCommands(this IServiceCollection services,
                                                   Action<ILiteBusCommandsBuilder> config)
        {
            var LiteBusBuilder = new LiteBusCommandsBuilder();

            config(LiteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(LiteBusBuilder.Assemblies.ToArray());

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