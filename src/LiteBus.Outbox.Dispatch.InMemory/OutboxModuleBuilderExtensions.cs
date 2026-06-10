using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Transport.InMemory;

namespace LiteBus.Outbox.Dispatch.InMemory;

/// <summary>
///     Registers the in-memory outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderInMemoryDispatchExtensions
{
    /// <summary>
    ///     Registers an in-memory transport outbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The optional dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseInMemoryDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new TransportOutboxDispatcherOptions();
        configure?.Invoke(options);
        return builder.RegisterDispatcher(new TransportOutboxDispatchModule(options, new InMemoryTransportModule()));
    }
}
