using System;
using System.Collections.Generic;

namespace LiteBus.Amqp;

/// <summary>
///     Describes one AMQP message publication.
/// </summary>
public sealed class AmqpPublishRequest
{
    /// <summary>
    ///     Gets the target exchange name.
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    ///     Gets the routing key used by the exchange to route the message.
    /// </summary>
    public required string RoutingKey { get; init; }

    /// <summary>
    ///     Gets the message body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the optional application headers copied onto the AMQP message.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Headers { get; init; }

    /// <summary>
    ///     Gets the MIME content type written to AMQP message properties.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>
    ///     Gets the optional content encoding written to AMQP message properties.
    /// </summary>
    public string? ContentEncoding { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the message should be persisted by the broker.
    /// </summary>
    public bool Persistent { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the broker must route the message to at least one queue.
    /// </summary>
    public bool Mandatory { get; init; }

    /// <summary>
    ///     Gets the optional AMQP message identifier written to message properties.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the optional AMQP correlation identifier written to message properties.
    /// </summary>
    public string? CorrelationId { get; init; }
}
