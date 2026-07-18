using System;
using System.Linq;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Inbox.Ingress.AzureServiceBus;

/// <summary>
///     Module that registers Azure Service Bus inbox ingress services.
/// </summary>
public sealed class AzureServiceBusInboxIngressModule :
    IInboxIngressModule,
    IRequires<InboxModule>,
    IRequires<AzureServiceBusTransportModule>
{
    /// <summary>
    ///     The configured ingress module builder.
    /// </summary>
    private readonly AzureServiceBusInboxIngressModuleBuilder _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AzureServiceBusInboxIngressModule(Action<AzureServiceBusInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _moduleBuilder = new AzureServiceBusInboxIngressModuleBuilder();
        builder(_moduleBuilder);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder;

        var options = moduleBuilder.Options ??
                      throw new LiteBusConfigurationException(
                          $"{nameof(AzureServiceBusInboxIngressOptions)} must be configured before registering Azure Service Bus inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AzureServiceBusInboxIngressOptions.Destination)} must be configured before registering Azure Service Bus inbox ingress.");
        }

        options.Safety.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(options.PrefetchCount);

        if (options.MaxConcurrentCalls is { } maxConcurrentCalls)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentCalls, 1);
        }

        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.Destination,
            SubscriptionName = options.SubscriptionName,
            PrefetchCount = options.PrefetchCount,
            MaxConcurrentCalls = options.MaxConcurrentCalls,
            RequeueOnFailure = options.RequeueOnFailure,
            Safety = options.Safety
        };

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TransportInboxIngressOptions), ingressOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxIngressHandler),
            typeof(TransportInboxIngressHandler)));

        if (moduleBuilder.EnableIngressConsumer)
        {
            moduleBuilder.HostOptions.Validate();

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressHostOptions),
                moduleBuilder.HostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportInboxIngressConsumer),
                typeof(TransportInboxIngressConsumer)));

            configuration.RegisterBackgroundService(typeof(TransportInboxIngressConsumer));
        }

    }
}
