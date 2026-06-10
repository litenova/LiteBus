namespace LiteBus.Transport.Aws;

/// <summary>
///     Public OpenTelemetry meter names for AWS SQS transport telemetry.
/// </summary>
/// <remarks>
///     This meter is reserved for future AWS SQS-specific instruments. No counters, histograms, or gauges are
///     registered by the current adapter. Circuit breaker metrics are recorded on the shared
///     <c>LiteBus.Transport</c> meter. Call <c>AddMeter(LiteBusAwsTelemetry.MeterName)</c> only when you add custom
///     AWS instrumentation.
/// </remarks>
public static class LiteBusAwsTelemetry
{
    /// <summary>
    ///     Gets the meter name used for AWS SQS transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.Aws";
}

