using System;
using System.Collections.Generic;
using Autofac;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Extensions.Hosting;
using Microsoft.Extensions.Hosting;
using BackgroundServiceHostAdapter = LiteBus.Runtime.Extensions.Hosting.BackgroundServiceHostAdapter;
using StartupTaskGate = LiteBus.Runtime.Extensions.Hosting.StartupTaskGate;
using StartupTaskPhaseHostedService = LiteBus.Runtime.Extensions.Hosting.StartupTaskPhaseHostedService;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Applies startup task and background service registrations from module configuration to an Autofac container
///     builder.
/// </summary>
public static class AutofacBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers startup task and background service types and their generic-host adapters with the container builder.
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

        if (startupTaskTypes.Count > 0)
        {
            builder.RegisterType<StartupTaskGate>()
                .SingleInstance();

            foreach (var implementationType in startupTaskTypes)
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

                    return new StartupTaskPhaseHostedService(
                        resolvedStartupTasks,
                        context.Resolve<StartupTaskGate>());
                })
                .As<IHostedService>()
                .SingleInstance();
        }
        else
        {
            builder.Register(_ =>
                {
                    var gate = new StartupTaskGate();
                    gate.SignalComplete();
                    return gate;
                })
                .As<StartupTaskGate>()
                .SingleInstance();
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            builder.RegisterType(implementationType)
                .AsSelf()
                .SingleInstance();
        }

        foreach (var implementationType in backgroundServiceTypes)
        {
            builder.Register(context => new BackgroundServiceHostAdapter(
                    (IBackgroundService) context.Resolve(implementationType),
                    context.Resolve<StartupTaskGate>()))
                .As<IHostedService>()
                .SingleInstance();
        }
    }
}
