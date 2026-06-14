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

    /// <summary>
    ///     Gets the instrument name incremented when inbox lease renewal fails during dispatch.
    /// </summary>
    public const string ProcessorLeaseLostInstrumentName = "litebus.inbox.processor.lease_lost";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist skips an envelope because the lease was lost.
    /// </summary>
    public const string ProcessorPersistSkippedInstrumentName = "litebus.inbox.processor.persist_skipped";

    /// <summary>
    ///     Gets the instrument name incremented when inbox retention cleanup fails.
    /// </summary>
    public const string CleanupErrorInstrumentName = "litebus.inbox.cleanup.errors";

    /// <summary>
    ///     Gets the histogram instrument name for inbox dispatch duration in milliseconds.
    /// </summary>
    public const string ProcessorDispatchDurationInstrumentName = "litebus.inbox.processor.dispatch_duration";

    /// <summary>
    ///     Gets the instrument name incremented when inbox envelopes are leased during a pass.
    /// </summary>
    public const string ProcessorLeasesAcquiredInstrumentName = "litebus.inbox.processor.leases_acquired";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist rejects an update because the lease was lost.
    /// </summary>
    public const string ProcessorPersistRejectedInstrumentName = "litebus.inbox.processor.persist_rejected";

    /// <summary>
    ///     Gets the instrument name incremented when inbox diagnostics probes fail to read queue depth from the store.
    /// </summary>
    public const string DiagnosticsUnavailableInstrumentName = "litebus.inbox.diagnostics.unavailable";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist throws and the processor continues the pass.
    /// </summary>
    public const string ProcessorPersistFailedInstrumentName = "litebus.inbox.processor.persist_failed";
}