using System;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Inbox.Ingress.AwsSqs;

/// <summary>
///     Module that registers AWS SQS inbox ingress services.
/// </summary>
public sealed class AwsSqsInboxIngressModule :
    IInboxIngressModule,
    ICompositeModule,
    IRequires<AwsSqsTransportModule>
{
    /// <inheritdoc />
    public CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ChildrenFirst;

    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<AwsSqsInboxIngressModuleBuilder> _builder;

    /// <summary>
    ///     The builder populated while the module graph declares children.
    /// </summary>
    private AwsSqsInboxIngressModuleBuilder? _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AwsSqsInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public AwsSqsInboxIngressModule(Action<AwsSqsInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        _moduleBuilder = new AwsSqsInboxIngressModuleBuilder();
        _builder(_moduleBuilder);

        var options = _moduleBuilder.Options ??
                      throw new LiteBusConfigurationException(
                          $"{nameof(AwsSqsInboxIngressOptions)} must be configured before registering AWS SQS inbox ingress.");

        if (!_moduleBuilder.UseRegisteredTransportOnly)
        {
            registerChild(new AwsSqsTransportModule(options.Connection));
        }
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder ??
                            throw new LiteBusConfigurationException(
                                "AwsSqsInboxIngressModule.Build was called without a prior DeclareChildren call. " +
                                "Register the module through IModuleRegistry.");

        var options = moduleBuilder.Options ??
                      throw new LiteBusConfigurationException(
                          $"{nameof(AwsSqsInboxIngressOptions)} must be configured before registering AWS SQS inbox ingress.");

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(AwsSqsInboxIngressOptions.Destination)} must be configured before registering AWS SQS inbox ingress.");
        }

        EnsureTransportRegistered(configuration);

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
            "AWS SQS inbox ingress requires IMessageConsumer to be registered. " +
            "Allow UseAwsSqsIngress to declare AwsSqsTransportModule or call UseRegisteredTransport after registering a shared transport module.");
    }
}
