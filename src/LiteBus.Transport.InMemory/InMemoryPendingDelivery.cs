namespace LiteBus.Transport.InMemory;

/// <summary>
///     One pending in-memory delivery waiting for a consumer handler.
/// </summary>
internal sealed class InMemoryPendingDelivery
{
    /// <summary>
    ///     Gets the delivery body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    ///     Gets the application headers copied from the publish request.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Headers { get; init; }

    /// <summary>
    ///     Gets the destination name the delivery was published to.
    /// </summary>
    public required string Destination { get; init; }

    /// <summary>
    ///     Gets the optional route within the destination.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    ///     Gets the optional transport message identifier.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    ///     Gets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the delivery is being redelivered.
    /// </summary>
    public bool Redelivered { get; init; }
}
