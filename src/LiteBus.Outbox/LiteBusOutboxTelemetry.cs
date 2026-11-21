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

    /// <summary>
    ///     Gets the instrument name incremented when outbox lease renewal fails during publication.
    /// </summary>
    public const string ProcessorLeaseLostInstrumentName = "litebus.outbox.processor.lease_lost";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist skips a message because the lease was lost.
    /// </summary>
    public const string ProcessorPersistSkippedInstrumentName = "litebus.outbox.processor.persist_skipped";

    /// <summary>
    ///     Gets the instrument name incremented when outbox retention cleanup fails.
    /// </summary>
    public const string CleanupErrorInstrumentName = "litebus.outbox.cleanup.errors";

    /// <summary>
    ///     Gets the histogram instrument name for outbox publication duration in milliseconds.
    /// </summary>
    public const string ProcessorDispatchDurationInstrumentName = "litebus.outbox.processor.dispatch_duration";

    /// <summary>
    ///     Gets the instrument name incremented when outbox messages are leased during a pass.
    /// </summary>
    public const string ProcessorLeasesAcquiredInstrumentName = "litebus.outbox.processor.leases_acquired";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist rejects an update because the lease was lost.
    /// </summary>
    public const string ProcessorPersistRejectedInstrumentName = "litebus.outbox.processor.persist_rejected";

    /// <summary>
    ///     Gets the instrument name incremented when outbox diagnostics probes fail to read queue depth from the store.
    /// </summary>
    public const string DiagnosticsUnavailableInstrumentName = "litebus.outbox.diagnostics.unavailable";

    /// <summary>
    ///     Gets the instrument name incremented when terminal persist throws and the processor continues the pass.
    /// </summary>
    public const string ProcessorPersistFailedInstrumentName = "litebus.outbox.processor.persist_failed";

    /// <summary>
    ///     Gets the instrument name counting envelopes handled by immediate dispatch after a commit, tagged with
    ///     <see cref="ImmediateDispatchOutcomeAttributeName" />.
    /// </summary>
    public const string ImmediateDispatchCountInstrumentName = "litebus.outbox.immediate_dispatch.count";

    /// <summary>
    ///     Gets the histogram instrument name for the time immediate dispatch took after one commit, in seconds.
    /// </summary>
    public const string ImmediateDispatchDurationInstrumentName = "litebus.outbox.immediate_dispatch.duration";

    /// <summary>
    ///     Gets the attribute key carrying how one immediately dispatched envelope ended: <c>published</c>,
    ///     <c>failed</c>, <c>skipped</c> or <c>timed_out</c>.
    /// </summary>
    public const string ImmediateDispatchOutcomeAttributeName = "litebus.outbox.immediate_dispatch.outcome";

    /// <summary>
    ///     Gets the instrument name incremented when an immediate-addressed envelope is staged with scheduled visibility.
    /// </summary>
    public const string ImmediateDispatchSkippedScheduledInstrumentName = "litebus.outbox.immediate_dispatch.skipped_scheduled";

    /// <summary>
    ///     Gets the attribute key carrying the stable contract name of an immediate-addressed envelope staged with scheduled visibility.
    /// </summary>
    public const string ImmediateDispatchContractAttributeName = "litebus.contract.name";
}
