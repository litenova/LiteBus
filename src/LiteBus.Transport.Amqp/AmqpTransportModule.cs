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
        ValidateOptions(options);
        _options = options;
    }

    /// <summary>
    ///     Validates connection and recovery settings before module composition.
    /// </summary>
    /// <param name="options">The connection settings to validate.</param>
    private static void ValidateOptions(AmqpConnectionOptions options)
    {
        if (options.Uri is null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.HostName);
            ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, ushort.MaxValue);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.VirtualHost);
            ArgumentNullException.ThrowIfNull(options.UserName);
            ArgumentNullException.ThrowIfNull(options.Password);
        }
        else if (!options.Uri.IsAbsoluteUri ||
                 options.Uri.Scheme is not "amqp" and not "amqps" ||
                 string.IsNullOrWhiteSpace(options.Uri.Host))
        {
            throw new ArgumentException(
                "The AMQP URI must be absolute, use the amqp or amqps scheme, and include a host.",
                nameof(options));
        }

        if (options.AutomaticRecoveryEnabled)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.NetworkRecoveryInterval, TimeSpan.Zero);
        }

        ArgumentNullException.ThrowIfNull(options.CircuitBreaker);
        ArgumentOutOfRangeException.ThrowIfNegative(options.CircuitBreaker.FailureThreshold);

        if (options.CircuitBreaker.FailureThreshold > 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
                options.CircuitBreaker.BreakDuration,
                TimeSpan.Zero);
        }
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
