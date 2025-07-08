using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiteBus(this IServiceCollection services,
                                                Action<IModuleRegistry> liteBusBuilderAction)
    {
        // Get the singleton registry instance
        var messageRegistry = MessageRegistryAccessor.Instance;

        // Register it as a singleton in DI
        services.TryAddSingleton<IMessageRegistry>(messageRegistry);

        // Create module registry with the shared message registry
        var liteBusBuilder = new ModuleRegistry(services, messageRegistry);
        liteBusBuilderAction(liteBusBuilder);
        liteBusBuilder.Initialize();

        return services;
    }
}