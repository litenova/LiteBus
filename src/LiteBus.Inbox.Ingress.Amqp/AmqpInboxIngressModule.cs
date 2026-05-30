using System;
using System.Linq;
using LiteBus.Amqp;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Module that registers AMQP inbox ingress services.
/// </summary>
public sealed class AmqpInboxIngressModule : IModule
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
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
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
            throw new InvalidOperationException(
                $"{nameof(AmqpInboxIngressOptions.QueueName)} must be configured before registering AMQP inbox ingress.");
        }

        RegisterAmqpServicesIfMissing(configuration, options.Connection);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(AmqpInboxIngressOptions), options));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AmqpInboxIngressHandler),
            typeof(AmqpInboxIngressHandler)));

        if (moduleBuilder.RegisterBackgroundWork)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(AmqpInboxIngressHostOptions),
                moduleBuilder.HostOptions));

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(AmqpInboxIngressBackgroundWork),
                typeof(AmqpInboxIngressBackgroundWork)));

            configuration.DependencyRegistry.RegisterBackgroundWork(typeof(AmqpInboxIngressBackgroundWork));
        }
    }

    /// <summary>
    ///     Registers shared AMQP services when another module has not already registered them.
    /// </summary>
    /// <param name="configuration">The module configuration receiving dependency registrations.</param>
    /// <param name="connectionOptions">The broker connection settings for the ingress consumer.</param>
    private static void RegisterAmqpServicesIfMissing(
        IModuleConfiguration configuration,
        AmqpConnectionOptions connectionOptions)
    {
        if (configuration.DependencyRegistry.Any(descriptor => descriptor.DependencyType == typeof(IAmqpConnectionManager)))
        {
            return;
        }

        var connectionManager = new AmqpConnectionManager(connectionOptions);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IAmqpConnectionManager), connectionManager));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IAmqpPublisher), new AmqpPublisher(connectionManager)));
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IAmqpConsumer), new AmqpConsumer(connectionManager)));
    }
}
