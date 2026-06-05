namespace LiteBus.Transport.Amqp;

/// <summary>
///     Public OpenTelemetry instrument names for AMQP transport telemetry.
/// </summary>
public static class LiteBusAmqpTelemetry
{
    /// <summary>
    ///     Gets the meter name used for AMQP circuit breaker metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.Amqp";

    /// <summary>
    ///     Gets the instrument name indicating whether the AMQP circuit breaker is open.
    /// </summary>
    public const string CircuitBreakerOpenInstrumentName = "litebus.amqp.circuit_breaker.open";

    /// <summary>
    ///     Gets the instrument name for the current AMQP circuit breaker failure count.
    /// </summary>
    public const string CircuitBreakerFailureCountInstrumentName = "litebus.amqp.circuit_breaker.failure_count";
}
