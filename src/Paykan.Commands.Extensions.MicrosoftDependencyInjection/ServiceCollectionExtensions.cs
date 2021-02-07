using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Paykan.Commands.Abstraction;
using Paykan.Registry;
using Paykan.Registry.Abstractions;

namespace Paykan.Commands.Extensions.MicrosoftDependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaykanCommands(this IServiceCollection services,
                                                   Action<IPaykanCommandsBuilder> config)
        {
            var paykanBuilder = new PaykanCommandsBuilder();

            config(paykanBuilder);

            var messageRegistry = MessageRegistryAccessor.MessageRegistry;

            messageRegistry.Register(paykanBuilder.Assemblies.ToArray());

            foreach (var descriptor in messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes) services.AddTransient(handlerType);

                foreach (var hookType in descriptor.PostHandleHookTypes) services.TryAddTransient(hookType);
            }

            var commandMediatorBuilder = new CommandMediatorBuilder();

            services.AddSingleton(f => commandMediatorBuilder.Build(f, messageRegistry));
            services.TryAddSingleton<IMessageRegistry>(MessageRegistryAccessor.MessageRegistry);

            return services;
        }
    }
}