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
public sealed class AmqpInboxIngressModule : IInboxIngressModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<AmqpInboxIngressModuleBuilder> _builder;

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
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new AmqpInboxIngressModuleBuilder();
        _builder(moduleBuilder);

        var options = moduleBuilder.Options;

        if (string.IsNullOrWhiteSpace(options.QueueName))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AmqpInboxIngressOptions.QueueName)} must be configured before registering AMQP inbox ingress.");
        }

        EnsureTransportRegistered(configuration, options);

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
    ///     Ensures <see cref="IMessageConsumer" /> is registered, bootstrapping AMQP transport from ingress options when
    ///     needed.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    /// <param name="options">The AMQP ingress options supplying connection settings when transport is not pre-registered.</param>
    private static void EnsureTransportRegistered(IModuleConfiguration configuration, AmqpInboxIngressOptions options)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        new AmqpTransportModule(options.Connection).Build(configuration);

        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IMessageConsumer)))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            "AMQP inbox ingress requires IMessageConsumer to be registered. " +
            "Configure AmqpInboxIngressOptions.Connection or register AmqpTransportModule before calling UseAmqpIngress().");
    }
}