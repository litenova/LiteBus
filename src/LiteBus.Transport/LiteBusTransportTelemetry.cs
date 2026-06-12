namespace LiteBus.Transport;

/// <summary>
///     Public OpenTelemetry instrument names for transport telemetry shared across broker adapters.
/// </summary>
public static class LiteBusTransportTelemetry
{
    /// <summary>
    ///     Gets the activity source name used for transport publish and consume spans.
    /// </summary>
    public const string ActivitySourceName = "LiteBus.Transport";

    /// <summary>
    ///     Gets the stable activity name recorded when a transport publisher sends a message.
    /// </summary>
    public const string PublishActivityName = "transport.publish";

    /// <summary>
    ///     Gets the stable activity name recorded when a transport consumer receives a message.
    /// </summary>
    public const string ConsumeActivityName = "transport.consume";

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

    /// <summary>
    ///     Gets the OpenTelemetry tag name identifying the transport broker adapter on circuit breaker metrics.
    /// </summary>
    public const string BrokerTagName = "litebus.transport.broker";
}