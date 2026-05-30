using System;
using LiteBus.Amqp;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Configures how <see cref="AmqpOutboxDispatcher" /> publishes leased outbox envelopes to an AMQP broker.
/// </summary>
/// <remarks>
///     RabbitMQ and LavinMQ both speak AMQP 0.9.1. Use the same options and registration extensions for either
///     broker; only the connection settings change.
/// </remarks>
public sealed class AmqpOutboxDispatcherOptions
{
    /// <summary>
    ///     Gets or sets the AMQP connection settings used by the dispatcher.
    /// </summary>
    public AmqpConnectionOptions Connection { get; set; } = new();

    /// <summary>
    ///     Gets or sets the default exchange name used when an envelope does not specify a topic override.
    /// </summary>
    /// <value>
    ///     Use an empty string for the default direct exchange that routes by queue name. Set a named exchange when
    ///     the broker topology uses topic or fanout exchanges.
    /// </value>
    public string DefaultExchange { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the MIME content type written to AMQP message properties.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    ///     Gets or sets a value indicating whether published messages should be persisted by the broker.
    /// </summary>
    public bool Persistent { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether unroutable messages must cause the publish operation to fail.
    /// </summary>
    public bool Mandatory { get; set; }

    /// <summary>
    ///     Gets or sets the optional routing key resolver used when <see cref="Outbox.Abstractions.OutboxEnvelope.Topic" />
    ///     is empty.
    /// </summary>
    /// <value>
    ///     When unset, the dispatcher uses <see cref="Outbox.Abstractions.OutboxEnvelope.ContractName" /> as the routing
    ///     key.
    /// </value>
    public Func<Outbox.Abstractions.OutboxEnvelope, string>? ResolveRoutingKey { get; set; }
}
