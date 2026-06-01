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

        var registeredBackgroundServices = new HashSet<Type>();

        foreach (var implementationType in backgroundServices)
        {
            if (!registeredBackgroundServices.Add(implementationType))
            {
                continue;
            }

            builder.RegisterType(implementationType)
                .AsSelf()
                .SingleInstance();

            builder.Register(context => new BackgroundServiceHostAdapter(
                    (IBackgroundService)context.Resolve(implementationType)))
                .As<IHostedService>()
                .SingleInstance();
        }
    }
}
