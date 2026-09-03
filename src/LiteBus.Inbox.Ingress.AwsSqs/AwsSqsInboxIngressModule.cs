using System;
using System.Linq;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Inbox.Ingress.AwsSqs;

/// <summary>
///     Module that registers AWS SQS inbox ingress services.
/// </summary>
public sealed class AwsSqsInboxIngressModule :
    IInboxIngressModule,
    IRequires<InboxModule>,
    IRequires<AwsSqsTransportModule>
{
    /// <summary>
    ///     The configured ingress module builder.
    /// </summary>
    private readonly AwsSqsInboxIngressModuleBuilder _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AwsSqsInboxIngressModule(Action<AwsSqsInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _moduleBuilder = new AwsSqsInboxIngressModuleBuilder();
        builder(_moduleBuilder);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder;

        var options = moduleBuilder.Options ??
                      throw new DurableStorageConfigurationException(
                          $"{nameof(AwsSqsInboxIngressOptions)} must be configured before registering AWS SQS inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new DurableStorageConfigurationException(
                $"{nameof(AwsSqsInboxIngressOptions.Destination)} must be configured before registering AWS SQS inbox ingress.");
        }

        options.Safety.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReceiveBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.ReceiveBatchSize, 10);

        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.Destination,
            ReceiveBatchSize = options.ReceiveBatchSize,
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
