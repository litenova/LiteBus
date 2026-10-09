using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     Channel-backed broker that routes in-memory deliveries to named destinations.
/// </summary>
public sealed class InMemoryTransportBroker : IDisposable
{
    /// <summary>
    ///     Gets the maximum number of unsettled deliveries admitted per destination.
    /// </summary>
    private readonly int _destinationCapacity;

    /// <summary>
    ///     Tracks whether disposal has started.
    /// </summary>
    private int _disposed;

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
    /// <exception cref="System.ObjectDisposedException">Thrown when the broker has been disposed.</exception>
    internal InMemoryDestinationEndpoint GetOrCreateEndpoint(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return _endpoints.GetOrAdd(
            destination,
            static (_, capacity) => new InMemoryDestinationEndpoint(capacity),
            _destinationCapacity);
    }

    /// <summary>
    ///     Clears all queued deliveries, resets destination endpoints, and revives the broker for test isolation.
    /// </summary>
    internal void Reset()
    {
        foreach (var endpoint in _endpoints.Values)
        {
            endpoint.Dispose();
        }

        _endpoints.Clear();

        // Reset revives the broker for test isolation; disposal stays terminal, so Dispose inlines its own teardown.
        Interlocked.Exchange(ref _disposed, 0);
    }

    /// <summary>
    ///     Completes every destination channel and releases destination resources.
    /// </summary>
    /// <remarks>
    ///     Disposal completes channel writers and disposes capacity guards, staying terminal unlike reset.
    ///     Repeated calls return without touching cleared state.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var endpoint in _endpoints.Values)
        {
            endpoint.Dispose();
        }

        _endpoints.Clear();
    }
}
