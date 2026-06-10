using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Registers the AMQP inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAmqpDispatchExtensions
{
    /// <summary>
    ///     Registers an AMQP-backed inbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="connectionOptions">The AMQP connection settings.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAmqpDispatch(
        this InboxModuleBuilder builder,
        Action<TransportInboxDispatcherOptions> configure,
        AmqpConnectionOptions connectionOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(connectionOptions);

        var options = new TransportInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportInboxDispatchModule(options, new AmqpTransportModule(connectionOptions)));
    }
}
