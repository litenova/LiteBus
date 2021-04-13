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
            var liteBusBuilder = new LiteBusCommandsBuilder();

            config(liteBusBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(liteBusBuilder.Assemblies.ToArray());
            messageRegistry.Register(liteBusBuilder.Types.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.TryAddTransient(handlerType);

                foreach (var postHookType in descriptor.PostHandleHookTypes) services.TryAddTransient(postHookType);
                
                foreach (var preHookType in descriptor.PreHandleHookTypes) services.TryAddTransient(preHookType);
            }

            services.TryAddTransient<ICommandMediator, CommandMediator>();
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}