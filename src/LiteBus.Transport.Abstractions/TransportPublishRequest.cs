using System.Diagnostics;

namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Describes one outbound transport message publication.
/// </summary>
[DebuggerDisplay("Destination = {Destination}, Route = {Route}, MessageId = {MessageId}")]
public sealed class TransportPublishRequest
{
    /// <summary>
    ///     Gets the primary destination address such as an AMQP exchange or Service Bus queue name.
    /// </summary>
    public required string Destination { get; init; }

    /// <summary>
    ///     Gets the optional route within the destination such as an AMQP routing key or Service Bus subject.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    ///     Gets the message body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the optional application headers copied onto the transport message.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Headers { get; init; }

    /// <summary>
    ///     Gets the MIME content type written to transport message properties.
    /// </summary>
    public string ContentType { get; init; } = "application/json";

    /// <summary>
    ///     Gets the optional content encoding written to transport message properties.
    /// </summary>
    public string? ContentEncoding { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the message should be persisted by the broker.
    /// </summary>
    public bool Persistent { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the broker must route the message to at least one consumer.
    /// </summary>
    public bool Mandatory { get; init; }

    /// <summary>
    ///     Gets the optional transport message identifier written to message properties.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier written to message properties.
    /// </summary>
    public string? CorrelationId { get; init; }
}