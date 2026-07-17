using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Outbox.Dispatch.Kafka;

/// <summary>
///     Registers the Kafka outbox dispatcher through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderKafkaDispatchExtensions
{
    /// <summary>
    ///     Registers a Kafka outbox dispatcher and the matching transport module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <param name="transportOptions">The Kafka connection settings.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseKafkaDispatch(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure,
        KafkaTransportOptions transportOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(transportOptions);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);

        return builder.RegisterDispatcher(
            new TransportOutboxDispatchModule<KafkaTransportModule>(
                options,
                new KafkaTransportModule(transportOptions)));
    }

    /// <summary>
    ///     Registers a Kafka outbox dispatcher that uses a <see cref="KafkaTransportModule" /> registered elsewhere in the
    ///     module graph.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The dispatcher configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseKafkaDispatchWithRegisteredTransport(
        this OutboxModuleBuilder builder,
        Action<TransportOutboxDispatcherOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TransportOutboxDispatcherOptions();
        configure(options);
        return builder.RegisterDispatcher(new TransportOutboxDispatchModule<KafkaTransportModule>(options));
    }
}
