namespace LiteBus.Transport.Aws;

/// <summary>
///     Public OpenTelemetry meter names for AWS SQS transport telemetry.
/// </summary>
/// <remarks>
///     Circuit breaker metrics are recorded on the shared <c>LiteBus.Transport</c> meter. This type documents the
///     adapter identity for OpenTelemetry registration and future adapter-specific instruments.
/// </remarks>
public static class LiteBusAwsTelemetry
{
    /// <summary>
    ///     Gets the meter name used for AWS SQS transport metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport.Aws";
}

