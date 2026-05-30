using System;
using LiteBus.Inbox;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Registers the AMQP inbox ingress background service for the generic host.
/// </summary>
public sealed class AmqpInboxIngressHostingModule : IModule, IRequires<InboxModule>, IRequires<AmqpInboxIngressModule>
{
    /// <summary>
    ///     Gets the optional callback that configures whether the ingress loop is enabled.
    /// </summary>
    private readonly Action<AmqpInboxIngressHostOptions>? _configure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressHostingModule" /> class.
    /// </summary>
    /// <param name="configure">An optional callback that configures whether the ingress loop is enabled.</param>
    public AmqpInboxIngressHostingModule(Action<AmqpInboxIngressHostOptions>? configure = null)
    {
        _configure = configure;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hostOptions = new AmqpInboxIngressHostOptions();
        _configure?.Invoke(hostOptions);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(AmqpInboxIngressHostOptions), hostOptions));
        configuration.DependencyRegistry.RegisterHostedService(typeof(AmqpInboxIngressConsumer));
    }
}
