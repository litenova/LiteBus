using System;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Module that registers AMQP transport services implementing <see cref="Abstractions.IMessageTransport" />.
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
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(Abstractions.IMessageTransport)))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpConnectionOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpConnectionManager),
            typeof(AmqpConnectionManager),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreaker),
            static serviceProvider =>
            {
                var manager = serviceProvider.GetService(typeof(IAmqpConnectionManager)) as AmqpConnectionManager;
                return manager?.TransportCircuitBreaker
                    ?? throw new InvalidOperationException("IAmqpConnectionManager is not registered.");
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IMessageTransport),
            typeof(AmqpPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IMessageConsumer),
            typeof(AmqpConsumer)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpPublisher),
            typeof(AmqpPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAmqpConsumer),
            typeof(AmqpConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
        AmqpTransportMetricsRegistration.RegisterIfNeeded(configuration);
    }
}
