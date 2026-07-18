using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.InMemory;

namespace LiteBus.Inbox.Dispatch.InMemory;

/// <summary>
///     Registers the in-memory inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderInMemoryDispatchExtensions
{
    /// <summary>
    ///     Registers an in-memory inbox dispatcher that uses the root in-memory transport.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The optional dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseInMemoryDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new TransportInboxDispatcherOptions();
        configure?.Invoke(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule<InMemoryTransportModule>(options));
    }
}
