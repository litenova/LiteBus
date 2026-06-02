using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Applies startup task and background service registrations from module configuration to a Microsoft dependency injection service collection.
/// </summary>
public static class MicrosoftBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers startup task and background service types and their generic-host adapters with the service collection.
    /// </summary>
    /// <param name="services">The service collection receiving host execution registrations.</param>
    /// <param name="startupTasks">The startup task implementation types registered by modules.</param>
    /// <param name="backgroundServices">The background service implementation types registered by modules.</param>
    public static void RegisterBackgroundServices(
        this IServiceCollection services,
        IReadOnlyList<Type> startupTasks,
        IReadOnlyList<Type> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(startupTasks);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        var startupTaskTypes = DeduplicatePreserveOrder(startupTasks);
        var backgroundServiceTypes = DeduplicatePreserveOrder(backgroundServices);

        if (startupTaskTypes.Count == 0 && backgroundServiceTypes.Count == 0)
        {
            return;
        }

        if (startupTaskTypes.Count > 0)
        {
            services.AddSingleton<StartupTaskGate>();

            foreach (var implementationType in startupTaskTypes)
            {
                services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
            }

            services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
            {
                var resolvedStartupTasks = new List<IStartupTask>(startupTaskTypes.Count);

                foreach (var implementationType in startupTaskTypes)
                {
                    resolvedStartupTasks.Add((IStartupTask)serviceProvider.GetRequiredService(implementationType));
                }

                return new StartupTaskPhaseHostedService(
                    resolvedStartupTasks,
                    serviceProvider.GetRequiredService<StartupTaskGate>());
            }));
        }
        else
        {
            services.AddSingleton(_ =>
            {
                var gate = new StartupTaskGate();
                gate.SignalComplete();
                return gate;
            });
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
                new BackgroundServiceHostAdapter(
                    (IBackgroundService)serviceProvider.GetRequiredService(implementationType),
                    serviceProvider.GetRequiredService<StartupTaskGate>())));
        }
    }

    /// <summary>
    ///     Returns types in first-seen order while skipping duplicates.
    /// </summary>
    /// <param name="types">The types to deduplicate.</param>
    /// <returns>The deduplicated type list.</returns>
    private static List<Type> DeduplicatePreserveOrder(IReadOnlyList<Type> types)
    {
        var result = new List<Type>(types.Count);
        var registeredTypes = new HashSet<Type>();

        foreach (var implementationType in types)
        {
            if (registeredTypes.Add(implementationType))
            {
                result.Add(implementationType);
            }
        }

        return result;
    }
}
