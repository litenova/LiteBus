using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Registers observable AMQP transport metrics once per module configuration.
/// </summary>
public static class AmqpTransportMetricsRegistration
{
    /// <summary>
    ///     Registers AMQP transport observable metrics when they have not already been configured.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the metrics registration.</param>
    public static void RegisterIfNeeded(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.TryGetContext<AmqpTransportMetricsRegisteredMarker>(out _))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpTransportObservableMetrics),
            static serviceProvider => new AmqpTransportObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(AmqpTransportObservableMetricsInitializer));
        configuration.SetContext(new AmqpTransportMetricsRegisteredMarker());
    }
}
