using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LiteBusHostOrchestrator = LiteBus.Runtime.Extensions.Hosting.LiteBusHostOrchestrator;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Applies startup task and background service registrations from module configuration to a Microsoft dependency
///     injection service collection.
/// </summary>
public static class MicrosoftBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers startup task and background service types and a single generic-host orchestrator with the service
    ///     collection.
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

        var startupTaskTypes = HostingRegistrationHelpers.DeduplicatePreserveOrder(startupTasks);
        var backgroundServiceTypes = HostingRegistrationHelpers.DeduplicatePreserveOrder(backgroundServices);

        if (startupTaskTypes.Count == 0 && backgroundServiceTypes.Count == 0)
        {
            return;
        }

        services.AddSingleton<BackgroundServiceHostedServiceIndex>();

        foreach (var implementationType in startupTaskTypes)
        {
            services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
        }

        services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
        {
            var resolvedStartupTasks = new List<IStartupTask>(startupTaskTypes.Count);

            foreach (var implementationType in startupTaskTypes)
            {
                resolvedStartupTasks.Add((IStartupTask) serviceProvider.GetRequiredService(implementationType));
            }

            var resolvedBackgroundServices = new List<IBackgroundService>(backgroundServiceTypes.Count);

            foreach (var implementationType in backgroundServiceTypes)
            {
                var backgroundService = (IBackgroundService) serviceProvider.GetRequiredService(implementationType);
                resolvedBackgroundServices.Add(backgroundService);

                serviceProvider.GetRequiredService<BackgroundServiceHostedServiceIndex>()
                    .Register(implementationType, backgroundService);
            }

            return new LiteBusHostOrchestrator(
                resolvedStartupTasks,
                resolvedBackgroundServices,
                serviceProvider.GetService<IHostApplicationLifetime>());
        }));
    }
}
