namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Public OpenTelemetry meter names for Azure Service Bus transport telemetry.
/// </summary>
/// <remarks>
///     Circuit breaker metrics are recorded on the shared <c>LiteBus.Transport</c> meter. This type documents the
///     adapter identity for OpenTelemetry registration and future adapter-specific instruments.
/// </remarks>
public static class LiteBusAzureServiceBusTelemetry
{
    /// <summary>
    ///     Gets the meter name used for Azure Service Bus transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.AzureServiceBus";
}

