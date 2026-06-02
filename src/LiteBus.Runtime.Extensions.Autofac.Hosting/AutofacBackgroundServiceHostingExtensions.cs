using System;
using System.Collections.Generic;
using Autofac;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Autofac.Hosting;

/// <summary>
///     Applies background service registrations from module configuration to an Autofac container builder.
/// </summary>
public static class AutofacBackgroundServiceHostingExtensions
{
    /// <summary>
    ///     Registers background service types and their generic-host adapters with the container builder.
    /// </summary>
    /// <param name="builder">The container builder receiving background service registrations.</param>
    /// <param name="backgroundServices">The background service implementation types registered by modules.</param>
    public static void RegisterBackgroundServices(this ContainerBuilder builder, IReadOnlyList<Type> backgroundServices)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(backgroundServices);

        var registration = BackgroundServiceHostingRegistrationSplitter.Split(backgroundServices);

        if (registration.StartupInitializerTypes.Count == 0 && registration.ContinuousServiceTypes.Count == 0)
        {
            return;
        }

        if (registration.StartupInitializerTypes.Count > 0)
        {
            builder.RegisterType<BackgroundServiceStartupGate>()
                .SingleInstance();

            foreach (var implementationType in registration.StartupInitializerTypes)
            {
                builder.RegisterType(implementationType)
                    .AsSelf()
                    .SingleInstance();
            }

            builder.Register(context =>
                {
                    var startupServices = new List<IBackgroundServiceStartupInitializer>(registration.StartupInitializerTypes.Count);

                    foreach (var implementationType in registration.StartupInitializerTypes)
                    {
                        startupServices.Add((IBackgroundServiceStartupInitializer)context.Resolve(implementationType));
                    }

                    return new BackgroundServiceStartupPhaseHostedService(
                        startupServices,
                        context.Resolve<BackgroundServiceStartupGate>());
                })
                .As<IHostedService>()
                .SingleInstance();
        }
        else
        {
            builder.Register(_ =>
                {
                    var gate = new BackgroundServiceStartupGate();
                    gate.SignalComplete();
                    return gate;
                })
                .As<BackgroundServiceStartupGate>()
                .SingleInstance();
        }

        foreach (var implementationType in registration.ContinuousServiceTypes)
        {
            builder.RegisterType(implementationType)
                .AsSelf()
                .SingleInstance();
        }

        foreach (var implementationType in registration.ContinuousServiceTypes)
        {
            builder.Register(context => new BackgroundServiceHostAdapter(
                    (IBackgroundService)context.Resolve(implementationType),
                    context.Resolve<BackgroundServiceStartupGate>()))
                .As<IHostedService>()
                .SingleInstance();
        }
    }
}
