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

    /// <summary>
    ///     Gets the instrument name for outbox queue depth grouped by status.
    /// </summary>
    public const string QueueDepthInstrumentName = "litebus.outbox.queue.depth";

    /// <summary>
    ///     Gets the instrument name for the outbox processor loop state.
    /// </summary>
    public const string ProcessorStateInstrumentName = "litebus.outbox.processor.state";

    /// <summary>
    ///     Gets the OpenTelemetry attribute key applied to outbox queue depth measurements.
    /// </summary>
    public const string QueueStatusAttributeName = "litebus.outbox.status";
}
