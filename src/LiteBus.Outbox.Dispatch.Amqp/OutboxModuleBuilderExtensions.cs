using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Registers the AMQP outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderAmqpDispatchExtensions
{
    /// <summary>
    ///     Registers the AMQP outbox dispatcher as an outbox child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The AMQP dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAmqpDispatcher(
        this OutboxModuleBuilder builder,
        Action<AmqpOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new AmqpOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new AmqpOutboxDispatchModule(options));
    }
}
