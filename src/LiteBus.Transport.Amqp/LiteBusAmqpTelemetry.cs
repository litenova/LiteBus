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
}
