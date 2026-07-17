using System;
using System.Linq;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Inbox.Ingress.InMemory;

/// <summary>
///     Module that registers in-memory inbox ingress services.
/// </summary>
public sealed class InMemoryInboxIngressModule :
    IInboxIngressModule,
    IRequires<InboxModule>,
    IRequires<InMemoryTransportModule>
{
    /// <summary>
    ///     The configured ingress module builder.
    /// </summary>
    private readonly InMemoryInboxIngressModuleBuilder _moduleBuilder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryInboxIngressModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public InMemoryInboxIngressModule(Action<InMemoryInboxIngressModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _moduleBuilder = new InMemoryInboxIngressModuleBuilder();
        builder(_moduleBuilder);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = _moduleBuilder;

        var options = moduleBuilder.Options;

        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(InMemoryInboxIngressOptions.Destination)} must be configured before registering in-memory inbox ingress.");
        }


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

    }
}
