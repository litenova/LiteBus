using System.Collections.Concurrent;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Channel-backed broker that routes in-memory deliveries to named destinations.
/// </summary>
public sealed class InMemoryTransportBroker
{
    /// <summary>
    ///     Gets the maximum number of unsettled deliveries admitted per destination.
    /// </summary>
    private readonly int _destinationCapacity;

    /// <summary>
    ///     Gets the destination endpoints keyed by destination name.
    /// </summary>
    private readonly ConcurrentDictionary<string, InMemoryDestinationEndpoint> _endpoints =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryTransportBroker" /> class with default options.
    /// </summary>
    public InMemoryTransportBroker()
        : this(new InMemoryTransportOptions())
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryTransportBroker" /> class.
    /// </summary>
    /// <param name="options">The process-local transport settings.</param>
    public InMemoryTransportBroker(InMemoryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DestinationCapacity, 1);
        _destinationCapacity = options.DestinationCapacity;
    }

    /// <summary>
    ///     Gets or creates the endpoint for the supplied destination name.
    /// </summary>
    /// <param name="destination">The destination name used by publishers and consumers.</param>
    /// <returns>The shared endpoint for the destination.</returns>
    internal InMemoryDestinationEndpoint GetOrCreateEndpoint(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return _endpoints.GetOrAdd(
            destination,
            static (_, capacity) => new InMemoryDestinationEndpoint(capacity),
            _destinationCapacity);
    }

    /// <summary>
    ///     Clears all queued deliveries and resets destination endpoints.
    /// </summary>
    internal void Reset()
    {
        foreach (var endpoint in _endpoints.Values)
        {
            endpoint.Dispose();
        }

        _endpoints.Clear();
    }
}
