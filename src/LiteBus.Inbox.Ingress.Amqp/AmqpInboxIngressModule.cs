using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Module that registers AMQP inbox ingress services.
/// </summary>
public sealed class AmqpInboxIngressModule :
    IInboxIngressModule,
    ICompositeModule,
    IRequires<AmqpTransportModule>
{
    /// <inheritdoc />
    public CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ChildrenFirst;

    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<AmqpInboxIngressModuleBuilder> _builder;

    /// <summary>
    ///     The builder populated while the module graph declares children.
    /// </summary>
    private AmqpInboxIngressModuleBuilder? _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AmqpInboxIngressModule(Action<AmqpInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        _moduleBuilder = new AmqpInboxIngressModuleBuilder();
        _builder(_moduleBuilder);

        if (!_moduleBuilder.UseRegisteredTransportOnly)
        {
            registerChild(new AmqpTransportModule(_moduleBuilder.Options.Connection));
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder ??
                            throw new LiteBusConfigurationException(
                                "AmqpInboxIngressModule.Build was called without a prior DeclareChildren call. " +
                                "Register the module through IModuleRegistry.");

        var options = moduleBuilder.Options;

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AmqpInboxIngressOptions.QueueName)} must be configured before registering AMQP inbox ingress.");
        }

        EnsureTransportRegistered(configuration);

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

        TransportMetricsRegistration.RegisterIfNeeded(configuration);
    }

    /// <summary>
    ///     Ensures <see cref="IMessageConsumer" /> was registered by the declared or shared transport module.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    private static void EnsureTransportRegistered(IModuleConfiguration configuration)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "AMQP inbox ingress requires IMessageConsumer to be registered. " +
            "Allow UseAmqpIngress to declare AmqpTransportModule or call UseRegisteredTransport after registering a shared transport module.");
    }
}
