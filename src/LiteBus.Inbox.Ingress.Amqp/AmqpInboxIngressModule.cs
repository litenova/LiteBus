using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Module that registers AMQP inbox ingress services.
/// </summary>
public sealed class AmqpInboxIngressModule :
    IInboxIngressModule,
    IRequires<InboxModule>,
    IRequires<AmqpTransportModule>
{
    /// <summary>
    ///     The configured ingress module builder.
    /// </summary>
    private readonly AmqpInboxIngressModuleBuilder _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AmqpInboxIngressModule(Action<AmqpInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _moduleBuilder = new AmqpInboxIngressModuleBuilder();
        builder(_moduleBuilder);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder;

        var options = moduleBuilder.Options;

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AmqpInboxIngressOptions.QueueName)} must be configured before registering AMQP inbox ingress.");
        }


        var ingressOptions = new TransportInboxIngressOptions
        {
            Destination = options.QueueName,
            PrefetchCount = options.PrefetchCount,
            DeclareDestination = options.DeclareQueue,
            DurableDestination = options.DurableQueue,
            RequeueOnFailure = options.RequeueOnFailure,
            TrustApplicationHeaders = options.TrustApplicationHeaders,
            EnableBatchAccept = options.EnableBatchAccept,
            BatchMaxWait = options.BatchMaxWait
        };

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(AmqpInboxIngressOptions), options));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(TransportInboxIngressOptions), ingressOptions));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportInboxIngressHandler),
            typeof(TransportInboxIngressHandler)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpInboxIngressHandler),
            typeof(AmqpInboxIngressHandler)));

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
