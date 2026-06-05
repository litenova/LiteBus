using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Dispatch.Transport;

/// <summary>
///     Registers the transport outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderTransportDispatchExtensions
{
    /// <summary>
    ///     Registers a transport-backed outbox dispatcher and optional transport child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The transport dispatcher configuration action.</param>
    /// <param name="transportModule">The optional transport module that registers <see cref="Abstractions.IMessageTransport" />.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseTransport(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure,
        IModule? transportModule = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportOutboxDispatchModule(options, transportModule));
    }
}
