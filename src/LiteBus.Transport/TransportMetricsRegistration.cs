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
    /// <param name="broker">The optional stable broker name recorded on transport metrics.</param>
    public static void RegisterIfNeeded(IModuleConfiguration configuration, string? broker = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(broker))
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportBrokerIdentity),
                new TransportBrokerIdentity(broker)));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportObservableMetrics),
            static serviceProvider => new TransportObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(TransportObservableMetricsInitializer));
    }
}
