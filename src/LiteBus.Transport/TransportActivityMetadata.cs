namespace LiteBus.Transport;

/// <summary>
///     Describes the broker-neutral message metadata recorded on one transport activity.
/// </summary>
public readonly record struct TransportActivityMetadata
{
    /// <summary>
    ///     Gets the OpenTelemetry messaging system identifier for the broker adapter.
    /// </summary>
    public required string MessagingSystem { get; init; }

    /// <summary>
    ///     Gets the destination name, such as a queue, topic, or exchange.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    ///     Gets the broker-specific route within the destination, when available.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    ///     Gets the broker message identifier, when available.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the conversation identifier used to correlate related messages, when available.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the broker previously attempted delivery.
    /// </summary>
    public bool Redelivered { get; init; }
}
