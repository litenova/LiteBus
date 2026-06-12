using System.Collections.Concurrent;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Channel-backed broker that routes in-memory deliveries to named destinations.
/// </summary>
public sealed class InMemoryTransportBroker
{
    /// <summary>
    ///     Gets the destination endpoints keyed by destination name.
    /// </summary>
    private readonly ConcurrentDictionary<string, InMemoryDestinationEndpoint> _endpoints =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets or creates the endpoint for the supplied destination name.
    /// </summary>
    /// <param name="destination">The destination name used by publishers and consumers.</param>
    /// <returns>The shared endpoint for the destination.</returns>
    internal InMemoryDestinationEndpoint GetOrCreateEndpoint(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return _endpoints.GetOrAdd(destination, static _ => new InMemoryDestinationEndpoint());
    }

    /// <summary>
    ///     Clears all queued deliveries and resets destination endpoints.
    /// </summary>
    internal void Reset()
    {
        _endpoints.Clear();
    }
}
