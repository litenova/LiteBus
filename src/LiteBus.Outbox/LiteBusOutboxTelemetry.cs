namespace LiteBus.Outbox;

/// <summary>
///     Public OpenTelemetry instrument names for outbox processing.
/// </summary>
public static class LiteBusOutboxTelemetry
{
    /// <summary>
    ///     Gets the activity source name used for outbox processor spans.
    /// </summary>
    public const string ActivitySourceName = "LiteBus.Outbox";

    /// <summary>
    ///     Gets the meter name used for outbox processor metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Outbox";
}
