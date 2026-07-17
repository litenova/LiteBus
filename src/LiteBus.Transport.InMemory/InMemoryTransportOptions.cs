namespace LiteBus.Transport.InMemory;

/// <summary>
///     Configures the process-local in-memory transport.
/// </summary>
public sealed record InMemoryTransportOptions
{
    /// <summary>
    ///     The default number of unsettled deliveries admitted per destination.
    /// </summary>
    public const int DefaultDestinationCapacity = 1024;

    /// <summary>
    ///     Gets the maximum number of queued and in-flight deliveries admitted for each destination.
    /// </summary>
    /// <remarks>
    ///     Publishers wait asynchronously when a destination reaches this limit. A delivery releases its capacity
    ///     only after it is accepted or discarded; requeue retains the existing capacity reservation.
    /// </remarks>
    public int DestinationCapacity { get; init; } = DefaultDestinationCapacity;
}
