namespace LiteBus.Transport.Kafka;

/// <summary>
///     Public OpenTelemetry meter names for Kafka transport telemetry.
/// </summary>
/// <remarks>
///     Circuit breaker metrics are recorded on the shared <c>LiteBus.Transport</c> meter. This type documents the
///     adapter identity for OpenTelemetry registration and future adapter-specific instruments.
/// </remarks>
public static class LiteBusKafkaTelemetry
{
    /// <summary>
    ///     Gets the meter name used for Kafka transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.Kafka";
}

