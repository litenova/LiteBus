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

    /// <summary>
    ///     Gets the instrument name for inbox queue depth grouped by status.
    /// </summary>
    public const string QueueDepthInstrumentName = "litebus.inbox.queue.depth";

    /// <summary>
    ///     Gets the instrument name for the inbox processor loop state.
    /// </summary>
    public const string ProcessorStateInstrumentName = "litebus.inbox.processor.state";

    /// <summary>
    ///     Gets the OpenTelemetry attribute key applied to inbox queue depth measurements.
    /// </summary>
    public const string QueueStatusAttributeName = "litebus.inbox.status";
}
