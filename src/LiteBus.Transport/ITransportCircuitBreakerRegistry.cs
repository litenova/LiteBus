namespace LiteBus.Transport;

/// <summary>
///     Resolves independent circuit breaker state for each outbound destination.
/// </summary>
public interface ITransportCircuitBreakerRegistry
{
    /// <summary>
    ///     Gets a value indicating whether any registered publisher circuit is open or half-open.
    /// </summary>
    /// <value><see langword="true" /> when at least one publisher scope is not closed; otherwise, <see langword="false" />.</value>
    bool IsAnyOpen { get; }

    /// <summary>
    ///     Gets the total failure count across registered publisher circuits.
    /// </summary>
    /// <value>The sum of current per-scope failure counts.</value>
    long FailureCount { get; }

    /// <summary>
    ///     Gets the circuit breaker assigned to one outbound destination.
    /// </summary>
    /// <param name="destination">The broker destination for the publish operation.</param>
    /// <returns>The stable circuit breaker for the publisher scope.</returns>
    ITransportCircuitBreaker GetPublisherCircuit(string destination);
}
