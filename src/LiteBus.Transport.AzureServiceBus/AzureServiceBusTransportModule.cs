using Azure.Messaging.ServiceBus;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Module that registers Azure Service Bus transport services implementing
///     <see cref="Abstractions.ITransportPublisher" />.
/// </summary>
public sealed class AzureServiceBusTransportModule : IModule
{
    /// <summary>
    ///     Gets the connection settings configured by the application.
    /// </summary>
    private readonly AzureServiceBusTransportOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusTransportModule" /> class.
    /// </summary>
    /// <param name="options">The connection settings configured by the application.</param>
    public AzureServiceBusTransportModule(AzureServiceBusTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            _options.ConsumerErrorRetryInterval,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            _options.ConsumerErrorRetryMaxInterval,
            _options.ConsumerErrorRetryInterval);
        ValidateConnectivityTarget(_options.ConnectivityCheckTarget);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AzureServiceBusTransportOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ServiceBusClient),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(AzureServiceBusTransportOptions))
                                  as AzureServiceBusTransportOptions ??
                              throw new InvalidOperationException($"{nameof(AzureServiceBusTransportOptions)} is not registered.");

                return new ServiceBusClient(options.ConnectionString, new ServiceBusClientOptions
                {
                    Identifier = options.ClientId
                });
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreakerRegistry),
            static _ => new TransportCircuitBreakerRegistry(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportPublisher),
            typeof(AzureServiceBusPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageConsumer),
            static serviceProvider =>
            {
                var client = serviceProvider.GetService(typeof(ServiceBusClient)) as ServiceBusClient ??
                             throw new InvalidOperationException($"{nameof(ServiceBusClient)} is not registered.");

                var options = serviceProvider.GetService(typeof(AzureServiceBusTransportOptions))
                                  as AzureServiceBusTransportOptions ??
                              throw new InvalidOperationException($"{nameof(AzureServiceBusTransportOptions)} is not registered.");

                return new AzureServiceBusConsumer(client, options);
            },
            InstanceLifetime.Singleton));

        TransportMetricsRegistration.RegisterIfNeeded(configuration, "azure_service_bus");

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AzureServiceBusConnectivityDiagnosticCheck),
            typeof(AzureServiceBusConnectivityDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(AzureServiceBusConnectivityDiagnosticCheck),
            "transport.azure_service_bus.connectivity");
    }

    /// <summary>
    ///     Validates a configured connectivity target before module composition.
    /// </summary>
    /// <param name="target">The optional target to validate.</param>
    private static void ValidateConnectivityTarget(AzureServiceBusDiagnosticTarget? target)
    {
        switch (target)
        {
            case null:
                return;
            case AzureServiceBusQueueDiagnosticTarget queue:
                ArgumentException.ThrowIfNullOrWhiteSpace(queue.QueueName);
                return;
            case AzureServiceBusSubscriptionDiagnosticTarget subscription:
                ArgumentException.ThrowIfNullOrWhiteSpace(subscription.TopicName);
                ArgumentException.ThrowIfNullOrWhiteSpace(subscription.SubscriptionName);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported diagnostic target.");
        }
    }
}
