namespace LiteBus.Transport;

/// <summary>
///     Public OpenTelemetry instrument names for transport telemetry shared across broker adapters.
/// </summary>
public static class LiteBusTransportTelemetry
{
    /// <summary>
    ///     Gets the meter name used for transport circuit breaker metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport";

    /// <summary>
    ///     Gets the instrument name indicating whether the transport circuit breaker is open.
    /// </summary>
    public const string CircuitBreakerOpenInstrumentName = "litebus.transport.circuit_breaker.open";

    /// <summary>
    ///     Gets the instrument name for the current transport circuit breaker failure count.
    /// </summary>
    public const string CircuitBreakerFailureCountInstrumentName = "litebus.transport.circuit_breaker.failure_count";
}
