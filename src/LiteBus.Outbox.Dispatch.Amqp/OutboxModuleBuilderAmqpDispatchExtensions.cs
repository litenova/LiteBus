using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Registers the AMQP outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderAmqpDispatchExtensions
{
    /// <summary>
    ///     Registers an AMQP-backed outbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="connectionOptions">The AMQP connection settings.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAmqpDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure,
        AmqpConnectionOptions connectionOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(connectionOptions);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(
            new TransportOutboxDispatchModule<AmqpTransportModule>(
                options,
                new AmqpTransportModule(connectionOptions)));
    }

    /// <summary>
    ///     Registers an AMQP-backed outbox dispatcher that uses an <see cref="AmqpTransportModule" /> registered elsewhere
    ///     in the module graph.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseAmqpDispatchWithRegisteredTransport(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportOutboxDispatchModule<AmqpTransportModule>(options));
    }
}
