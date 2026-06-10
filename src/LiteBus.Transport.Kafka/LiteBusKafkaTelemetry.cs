namespace LiteBus.Transport.Kafka;

/// <summary>
///     Public OpenTelemetry meter names for Kafka transport telemetry.
/// </summary>
/// <remarks>
///     This meter is reserved for future Kafka-specific instruments. No counters, histograms, or gauges are registered
///     by the current adapter. Circuit breaker metrics are recorded on the shared <c>LiteBus.Transport</c> meter.
///     Call <c>AddMeter(LiteBusKafkaTelemetry.MeterName)</c> only when you add custom Kafka instrumentation.
/// </remarks>
public static class LiteBusKafkaTelemetry
{
    /// <summary>
    ///     Gets the meter name used for Kafka transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.Kafka";
}

