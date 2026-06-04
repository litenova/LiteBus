using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Registers the AMQP inbox dispatcher through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderAmqpDispatchExtensions
{
    /// <summary>
    ///     Registers the AMQP inbox dispatcher as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The AMQP dispatcher configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseAmqpDispatcher(
        this InboxModuleBuilder builder,
        Action<AmqpInboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AmqpInboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new AmqpInboxDispatchModule(options));
    }
}
