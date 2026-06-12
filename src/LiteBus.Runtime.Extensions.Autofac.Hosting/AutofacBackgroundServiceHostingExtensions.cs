using System;
using System.Collections.Generic;
using Autofac;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.Hosting;
using LiteBusHostOrchestrator = LiteBus.Runtime.Extensions.Hosting.LiteBusHostOrchestrator;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Applies startup task and background service registrations from module configuration to an Autofac container
///     builder.
/// </summary>
public static class AutofacBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers startup task and background service types and a single generic-host orchestrator with the container
    ///     builder.
    /// </summary>
    /// <param name="builder">The container builder receiving host execution registrations.</param>
    /// <param name="startupTasks">The startup task implementation types registered by modules.</param>
    /// <param name="backgroundServices">The background service implementation types registered by modules.</param>
    public static void RegisterBackgroundServices(
        this ContainerBuilder builder,
        IReadOnlyList<Type> startupTasks,
        IReadOnlyList<Type> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(startupTasks);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        var startupTaskTypes = HostingRegistrationHelpers.DeduplicatePreserveOrder(startupTasks);
        var backgroundServiceTypes = HostingRegistrationHelpers.DeduplicatePreserveOrder(backgroundServices);

        if (startupTaskTypes.Count == 0 && backgroundServiceTypes.Count == 0)
        {
            return;
        }

        foreach (var implementationType in startupTaskTypes)
        {
            builder.RegisterType(implementationType)
                .AsSelf()
                .SingleInstance();
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            builder.RegisterType(implementationType)
                .AsSelf()
                .SingleInstance();
        }

        builder.Register(context =>
            {
                var resolvedStartupTasks = new List<IStartupTask>(startupTaskTypes.Count);

                foreach (var implementationType in startupTaskTypes)
                {
                    resolvedStartupTasks.Add((IStartupTask) context.Resolve(implementationType));
                }

                var resolvedBackgroundServices = new List<IBackgroundService>(backgroundServiceTypes.Count);

                foreach (var implementationType in backgroundServiceTypes)
                {
                    resolvedBackgroundServices.Add((IBackgroundService) context.Resolve(implementationType));
                }

                return new LiteBusHostOrchestrator(resolvedStartupTasks, resolvedBackgroundServices);
            })
            .As<IHostedService>()
            .SingleInstance();
    }
}
