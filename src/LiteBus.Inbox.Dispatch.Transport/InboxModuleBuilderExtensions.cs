using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Dispatch.Transport;

/// <summary>
///     Registers the transport inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderTransportDispatchExtensions
{
    /// <summary>
    ///     Registers a transport-backed inbox dispatcher and optional transport child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The transport dispatcher configuration action.</param>
    /// <param name="transportModule">The optional transport module that registers <see cref="Abstractions.IMessageTransport" />.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseTransport(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure,
        IModule? transportModule = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule(options, transportModule));
    }
}
