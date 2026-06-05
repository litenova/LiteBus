namespace LiteBus.Inbox;

/// <summary>
///     Public OpenTelemetry instrument names for inbox processing.
/// </summary>
public static class LiteBusInboxTelemetry
{
    /// <summary>
    ///     Gets the activity source name used for inbox processor spans.
    /// </summary>
    public const string ActivitySourceName = "LiteBus.Inbox";

    /// <summary>
    ///     Gets the meter name used for inbox processor metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Inbox";
}
