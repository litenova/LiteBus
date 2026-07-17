using System;
using System.Linq;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Inbox.Ingress.Kafka;

/// <summary>
///     Module that registers Kafka inbox ingress services.
/// </summary>
public sealed class KafkaInboxIngressModule :
    IInboxIngressModule,
    IRequires<InboxModule>,
    IRequires<KafkaTransportModule>
{
    /// <summary>
    ///     The configured ingress module builder.
    /// </summary>
    private readonly KafkaInboxIngressModuleBuilder _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public KafkaInboxIngressModule(Action<KafkaInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _moduleBuilder = new KafkaInboxIngressModuleBuilder();
        builder(_moduleBuilder);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder;

        var options = moduleBuilder.Options ??
                      throw new LiteBusConfigurationException(
                          $"{nameof(KafkaInboxIngressOptions)} must be configured before registering Kafka inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(KafkaInboxIngressOptions.Destination)} must be configured before registering Kafka inbox ingress.");
        }

        options.Safety.Validate();

        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.Destination,
            RequeueOnFailure = options.RequeueOnFailure,
            Safety = options.Safety
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

    }
}
