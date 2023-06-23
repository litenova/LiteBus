using System;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiteBus(this IServiceCollection services,
                                                Action<IModuleRegistry> liteBusBuilderAction)
    {
        var liteBusBuilder = new ModuleRegistry(services);

        liteBusBuilderAction(liteBusBuilder);

        liteBusBuilder.Initialize();

        return services;
    }
}