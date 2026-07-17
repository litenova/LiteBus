using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Module that registers AMQP transport services implementing <see cref="Abstractions.ITransportPublisher" />.
/// </summary>
public sealed class AmqpTransportModule : IModule
{
    /// <summary>
    ///     Gets the connection settings configured by the application.
    /// </summary>
    private readonly AmqpConnectionOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpTransportModule" /> class.
    /// </summary>
    /// <param name="options">The connection settings configured by the application.</param>
    public AmqpTransportModule(AmqpConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpConnectionOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpConnectionManager),
            typeof(AmqpConnectionManager),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreakerRegistry),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(AmqpConnectionOptions)) as AmqpConnectionOptions ??
                              throw new InvalidOperationException("AmqpConnectionOptions is not registered.");

                return new TransportCircuitBreakerRegistry(options.CircuitBreaker.ToTransportOptions());
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportPublisher),
            typeof(AmqpPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            typeof(AmqpConsumer)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpPublisher),
            typeof(AmqpPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpConsumer),
            typeof(AmqpConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "amqp");

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpConnectivityDiagnosticCheck),
            typeof(AmqpConnectivityDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(typeof(AmqpConnectivityDiagnosticCheck), "transport.amqp.connectivity");
    }
}
