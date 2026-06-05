using System;
using System.Linq;
using Azure.Messaging.ServiceBus;
using LiteBus.Runtime.Abstractions;
using LiteBus.Transport;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Module that registers Azure Service Bus transport services implementing
///     <see cref="Abstractions.IMessageTransport" />.
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
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
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
            typeof(AzureServiceBusTransportOptions),
            _options));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ServiceBusClient),
            static serviceProvider =>
            {
                var options = serviceProvider.GetService(typeof(AzureServiceBusTransportOptions))
                    as AzureServiceBusTransportOptions
                    ?? throw new InvalidOperationException($"{nameof(AzureServiceBusTransportOptions)} is not registered.");

                return new ServiceBusClient(options.ConnectionString, new ServiceBusClientOptions
                {
                    Identifier = options.ClientId
                });
            },
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransportCircuitBreaker),
            static _ => new TransportCircuitBreaker(),
            InstanceLifetime.Singleton));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IMessageTransport),
            typeof(AzureServiceBusPublisher)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(Abstractions.IMessageConsumer),
            typeof(AzureServiceBusConsumer)));

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
    }
}

