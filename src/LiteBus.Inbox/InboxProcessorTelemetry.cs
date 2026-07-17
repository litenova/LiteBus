using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     OpenTelemetry activity and metric instrumentation for inbox processing.
/// </summary>
/// <remarks>
///     Pass-level counter instrument names are stable and documented in
///     <c>docs/architecture/README.md</c> as internal-only until promoted to
///     <see cref="LiteBusInboxTelemetry" /> public constants.
/// </remarks>
internal static class InboxProcessorTelemetry
{
    /// <summary>
    ///     Gets the instrument name incremented once per processor pass.
    /// </summary>
    private const string PassInstrumentName = "litebus.inbox.processor.passes";

    /// <summary>
    ///     Gets the instrument name incremented for each successfully completed envelope.
    /// </summary>
    private const string SucceededInstrumentName = "litebus.inbox.processor.succeeded";

    /// <summary>
    ///     Gets the instrument name incremented for each failed envelope scheduled for retry.
    /// </summary>
    private const string FailedInstrumentName = "litebus.inbox.processor.failed";

    /// <summary>
    ///     Gets the instrument name incremented for each dead-lettered envelope.
    /// </summary>
    private const string DeadLetteredInstrumentName = "litebus.inbox.processor.dead_lettered";

    /// <summary>
    ///     Gets the instrument name incremented when the background loop catches an unhandled exception.
    /// </summary>
    private const string LoopErrorInstrumentName = "litebus.inbox.processor.loop_errors";

    /// <summary>
    ///     Gets the activity source used for inbox processor spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(LiteBusInboxTelemetry.ActivitySourceName);

    /// <summary>
    ///     Gets the meter used for inbox processor counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusInboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented once per processor pass.
    /// </summary>
    private static readonly Counter<long> PassCounter = Meter.CreateCounter<long>(PassInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each successfully completed envelope.
    /// </summary>
    private static readonly Counter<long> SucceededCounter = Meter.CreateCounter<long>(SucceededInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each failed envelope scheduled for retry.
    /// </summary>
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>(FailedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each dead-lettered envelope.
    /// </summary>
    private static readonly Counter<long> DeadLetteredCounter = Meter.CreateCounter<long>(DeadLetteredInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when the background loop catches an unhandled exception.
    /// </summary>
    private static readonly Counter<long> LoopErrorCounter = Meter.CreateCounter<long>(LoopErrorInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when lease renewal fails during dispatch.
    /// </summary>
    private static readonly Counter<long> LeaseLostCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorLeaseLostInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist skips an envelope because the lease was lost.
    /// </summary>
    private static readonly Counter<long> PersistSkippedCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorPersistSkippedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when inbox envelopes are leased during a pass.
    /// </summary>
    private static readonly Counter<long> LeasesAcquiredCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorLeasesAcquiredInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist rejects an update because the lease was lost.
    /// </summary>
    private static readonly Counter<long> PersistRejectedCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorPersistRejectedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist throws and the processor continues the pass.
    /// </summary>
    private static readonly Counter<long> PersistFailedCounter =
        Meter.CreateCounter<long>(LiteBusInboxTelemetry.ProcessorPersistFailedInstrumentName);

    /// <summary>
    ///     Gets the histogram recording inbox dispatch duration in milliseconds.
    /// </summary>
    private static readonly Histogram<double> DispatchDurationHistogram =
        Meter.CreateHistogram<double>(LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName, "ms");

    /// <summary>
    ///     Records that the inbox processor background loop caught an unhandled exception.
    /// </summary>
    public static void RecordLoopError()
    {
        LoopErrorCounter.Add(1);
    }

    /// <summary>
    ///     Records that inbox lease renewal failed and dispatch was canceled.
    /// </summary>
    public static void RecordLeaseLost()
    {
        LeaseLostCounter.Add(1);
    }

    /// <summary>
    ///     Records that terminal persist skipped an envelope because the active lease no longer matched.
    /// </summary>
    public static void RecordPersistSkipped()
    {
        PersistSkippedCounter.Add(1);
        PersistRejectedCounter.Add(1);
    }

    /// <summary>
    ///     Records that terminal persist threw and the processor continued with remaining envelopes.
    /// </summary>
    public static void RecordPersistFailed()
    {
        PersistFailedCounter.Add(1);
    }

    /// <summary>
    ///     Records the number of inbox envelopes leased during one processor pass.
    /// </summary>
    /// <param name="leasedCount">The number of envelopes leased.</param>
    public static void RecordLeasesAcquired(int leasedCount)
    {
        if (leasedCount > 0)
        {
            LeasesAcquiredCounter.Add(leasedCount);
        }
    }

    /// <summary>
    ///     Records inbox dispatch duration for one envelope.
    /// </summary>
    /// <param name="duration">The dispatch duration.</param>
    public static void RecordDispatchDuration(TimeSpan duration)
    {
        DispatchDurationHistogram.Record(duration.TotalMilliseconds);
    }

    /// <summary>
    ///     Records pass-level metrics after one processor pass completes.
    /// </summary>
    /// <param name="leasedCount">The number of envelopes leased during the pass.</param>
    /// <param name="succeededCount">The number of envelopes marked completed.</param>
    /// <param name="failedCount">The number of envelopes marked failed.</param>
    /// <param name="deadLetteredCount">The number of envelopes moved to dead letter.</param>
    public static void RecordPass(int leasedCount, int succeededCount, int failedCount, int deadLetteredCount)
    {
        PassCounter.Add(1);
        SucceededCounter.Add(succeededCount);
        FailedCounter.Add(failedCount);
        DeadLetteredCounter.Add(deadLetteredCount);
    }

    /// <summary>
    ///     Records pass-level activity tags and metrics after one processor pass completes.
    /// </summary>
    /// <param name="passActivity">The pass activity started for the current iteration.</param>
    /// <param name="result">The aggregated pass result.</param>
    public static void RecordPassResult(Activity? passActivity, ProcessorPassResult result)
    {
        passActivity?.SetTag("litebus.inbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.inbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.inbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.inbox.dead_lettered_count", result.DeadLetteredCount);
        RecordPass(result.LeasedCount, result.SucceededCount, result.FailedCount, result.DeadLetteredCount);
    }
}
