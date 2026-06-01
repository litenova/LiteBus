using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Applies background service registrations from module configuration to a Microsoft dependency injection service collection.
/// </summary>
public static class MicrosoftBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers background service types and their generic-host adapters with the service collection.
    /// </summary>
    /// <param name="services">The service collection receiving background service registrations.</param>
    /// <param name="backgroundServices">The background service implementation types registered by modules.</param>
    public static void RegisterBackgroundServices(this IServiceCollection services, IReadOnlyList<Type> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        var registeredBackgroundServices = new HashSet<Type>();

        foreach (var implementationType in backgroundServices)
        {
            if (!registeredBackgroundServices.Add(implementationType))
            {
                continue;
            }

            services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
            services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
                new BackgroundServiceHostAdapter((IBackgroundService)serviceProvider.GetRequiredService(implementationType))));
        }
    }
}
