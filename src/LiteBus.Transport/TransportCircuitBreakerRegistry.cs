using System.Collections.Concurrent;

namespace LiteBus.Transport;

/// <summary>
///     Stores independent publisher circuit breakers by destination.
/// </summary>
public sealed class TransportCircuitBreakerRegistry : ITransportCircuitBreakerRegistry
{
    /// <summary>
    ///     Gets the circuit breaker settings copied into each publisher scope.
    /// </summary>
    private readonly TransportCircuitBreakerOptions _options;

    /// <summary>
    ///     Gets the publisher circuits keyed by destination.
    /// </summary>
    private readonly ConcurrentDictionary<string, ITransportCircuitBreaker> _publisherCircuits =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the time source shared by publisher circuits.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportCircuitBreakerRegistry" /> class.
    /// </summary>
    /// <param name="options">The settings copied into each publisher circuit.</param>
    /// <param name="timeProvider">The monotonic time source used to measure break durations.</param>
    public TransportCircuitBreakerRegistry(
        TransportCircuitBreakerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? new TransportCircuitBreakerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsAnyOpen => _publisherCircuits.Values.Any(circuitBreaker => circuitBreaker.IsOpen);

    /// <inheritdoc />
    public long FailureCount => _publisherCircuits.Values.Sum(circuitBreaker => (long)circuitBreaker.FailureCount);

    /// <inheritdoc />
    public ITransportCircuitBreaker GetPublisherCircuit(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return _publisherCircuits.GetOrAdd(
            destination,
            _ => new TransportCircuitBreaker(_options, _timeProvider));
    }
}
