namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Public OpenTelemetry meter names for Azure Service Bus transport telemetry.
/// </summary>
/// <remarks>
///     This meter is reserved for future Azure Service Bus-specific instruments. No counters, histograms, or gauges are
///     registered by the current adapter. Circuit breaker metrics are recorded on the shared
///     <c>LiteBus.Transport</c> meter. Call <c>AddMeter(LiteBusAzureServiceBusTelemetry.MeterName)</c> only when you
///     add custom Azure instrumentation.
/// </remarks>
public static class LiteBusAzureServiceBusTelemetry
{
    /// <summary>
    ///     Gets the meter name used for Azure Service Bus transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.AzureServiceBus";
}