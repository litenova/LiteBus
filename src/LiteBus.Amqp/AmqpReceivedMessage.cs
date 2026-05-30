using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Amqp;

/// <summary>
///     Represents one AMQP delivery received by a consumer.
/// </summary>
public sealed class AmqpReceivedMessage
{
    /// <summary>
    ///     Gets the delivery body. Copy or deserialize the payload before the handler returns.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the application headers copied from the AMQP message.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Headers { get; init; }

    /// <summary>
    ///     Gets the broker delivery tag used for acknowledgement operations.
    /// </summary>
    public required ulong DeliveryTag { get; init; }

    /// <summary>
    ///     Gets the exchange the message was published to.
    /// </summary>
    public string? Exchange { get; init; }

    /// <summary>
    ///     Gets the routing key the message was published with.
    /// </summary>
    public string? RoutingKey { get; init; }

    /// <summary>
    ///     Gets the AMQP message identifier from message properties, when present.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the AMQP correlation identifier from message properties, when present.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the broker previously attempted delivery.
    /// </summary>
    public bool Redelivered { get; init; }

    /// <summary>
    ///     Gets the delegate that acknowledges successful processing of the delivery.
    /// </summary>
    public required Func<bool, CancellationToken, Task> AckAsync { get; init; }

    /// <summary>
    ///     Gets the delegate that negative-acknowledges the delivery so the broker can requeue or dead-letter it.
    /// </summary>
    public required Func<bool, bool, CancellationToken, Task> NackAsync { get; init; }
}
