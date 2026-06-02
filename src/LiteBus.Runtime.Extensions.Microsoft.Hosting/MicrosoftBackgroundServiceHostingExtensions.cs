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

        var registration = BackgroundServiceHostingRegistrationSplitter.Split(backgroundServices);

        if (registration.StartupInitializerTypes.Count == 0 && registration.ContinuousServiceTypes.Count == 0)
        {
            return;
        }

        if (registration.StartupInitializerTypes.Count > 0)
        {
            services.AddSingleton<BackgroundServiceStartupGate>();

            foreach (var implementationType in registration.StartupInitializerTypes)
            {
                services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
            }

            services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
            {
                var startupServices = new List<IBackgroundServiceStartupInitializer>(registration.StartupInitializerTypes.Count);

                foreach (var implementationType in registration.StartupInitializerTypes)
                {
                    startupServices.Add((IBackgroundServiceStartupInitializer)serviceProvider.GetRequiredService(implementationType));
                }

                return new BackgroundServiceStartupPhaseHostedService(
                    startupServices,
                    serviceProvider.GetRequiredService<BackgroundServiceStartupGate>());
            }));
        }
        else
        {
            services.AddSingleton(_ =>
            {
                var gate = new BackgroundServiceStartupGate();
                gate.SignalComplete();
                return gate;
            });
        }

        foreach (var implementationType in registration.ContinuousServiceTypes)
        {
            services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
        }

        foreach (var implementationType in registration.ContinuousServiceTypes)
        {
            services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
                new BackgroundServiceHostAdapter(
                    (IBackgroundService)serviceProvider.GetRequiredService(implementationType),
                    serviceProvider.GetRequiredService<BackgroundServiceStartupGate>())));
        }
    }
}
