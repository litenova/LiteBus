using System;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Ingress;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Module that registers Azure Service Bus inbox ingress services.
/// </summary>
public sealed class AzureServiceBusInboxIngressModule : IInboxIngressModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<AzureServiceBusInboxIngressModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AzureServiceBusInboxIngressModule(Action<AzureServiceBusInboxIngressModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new AzureServiceBusInboxIngressModuleBuilder();
        _builder(moduleBuilder);

        var options = moduleBuilder.Options
            ?? throw new LiteBusConfigurationException(
                $"{nameof(AzureServiceBusInboxIngressOptions)} must be configured before registering Azure Service Bus inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AzureServiceBusInboxIngressOptions.Destination)} must be configured before registering Azure Service Bus inbox ingress.");
        }

        EnsureTransportRegistered(configuration, options);

        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.Destination,
            PrefetchCount = options.PrefetchCount,
            RequeueOnFailure = options.RequeueOnFailure
        };

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TransportInboxIngressOptions), ingressOptions));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxIngressHandler),
            typeof(TransportInboxIngressHandler)));

        if (moduleBuilder.EnableIngressConsumer)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressHostOptions),
                moduleBuilder.HostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressConsumer),
                typeof(TransportInboxIngressConsumer)));

            configuration.RegisterBackgroundService(typeof(TransportInboxIngressConsumer));
        }

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
    }

    /// <summary>
    ///     Ensures <see cref="IMessageConsumer" /> is registered, bootstrapping Service Bus transport from ingress options when needed.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    /// <param name="options">The ingress options supplying connection settings when transport is not pre-registered.</param>
    private static void EnsureTransportRegistered(
        IModuleConfiguration configuration,
        AzureServiceBusInboxIngressOptions options)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        new AzureServiceBusTransportModule(options.Connection).Build(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "Azure Service Bus inbox ingress requires IMessageConsumer to be registered. " +
            "Configure AzureServiceBusInboxIngressOptions.Connection or register AzureServiceBusTransportModule before calling UseAzureServiceBusIngress().");
    }
}
