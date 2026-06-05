using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Registers observable transport metrics once per module configuration.
/// </summary>
public static class TransportMetricsRegistration
{
    /// <summary>
    ///     Registers transport observable metrics when they have not already been configured.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the metrics registration.</param>
    public static void RegisterIfNeeded(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.TryGetContext<TransportMetricsRegisteredMarker>(out _))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportObservableMetrics),
            static serviceProvider => new TransportObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(TransportObservableMetricsInitializer));
        configuration.SetContext(new TransportMetricsRegisteredMarker());
    }
}
