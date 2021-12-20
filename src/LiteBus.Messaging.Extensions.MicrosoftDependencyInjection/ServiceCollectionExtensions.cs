using System;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiteBus(this IServiceCollection services,
                                                Action<ILiteBusConfiguration> liteBusBuilderAction)
    {
        var liteBusBuilder = new LiteBusConfiguration(services);

        liteBusBuilderAction(liteBusBuilder);

        liteBusBuilder.Initialize();

        return services;
    }
}