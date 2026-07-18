using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Registers the AMQP inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAmqpDispatchExtensions
{
    /// <summary>
    ///     Registers an AMQP-backed inbox dispatcher that uses the root AMQP transport.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAmqpDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule<AmqpTransportModule>(options));
    }
}
