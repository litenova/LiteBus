using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LiteBus.Outbox;

/// <summary>
///     OpenTelemetry activity and metric instrumentation for outbox processing.
/// </summary>
/// <remarks>
///     Pass-level counter instrument names are stable and documented in
///     <c>docs/architecture/README.md</c> as internal-only until promoted to
///     <see cref="LiteBusOutboxTelemetry" /> public constants.
/// </remarks>
internal static class OutboxProcessorTelemetry
{
    /// <summary>
    ///     Gets the instrument name incremented once per processor pass.
    /// </summary>
    private const string PassInstrumentName = "litebus.outbox.processor.passes";

    /// <summary>
    ///     Gets the instrument name incremented for each successfully published message.
    /// </summary>
    private const string PublishedInstrumentName = "litebus.outbox.processor.published";

    /// <summary>
    ///     Gets the instrument name incremented for each failed message scheduled for retry.
    /// </summary>
    private const string FailedInstrumentName = "litebus.outbox.processor.failed";

    /// <summary>
    ///     Gets the instrument name incremented for each dead-lettered message.
    /// </summary>
    private const string DeadLetteredInstrumentName = "litebus.outbox.processor.dead_lettered";

    /// <summary>
    ///     Gets the instrument name incremented when the background loop catches an unhandled exception.
    /// </summary>
    private const string LoopErrorInstrumentName = "litebus.outbox.processor.loop_errors";

    /// <summary>
    ///     Gets the activity source used for outbox processor spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(LiteBusOutboxTelemetry.ActivitySourceName);

    /// <summary>
    ///     Gets the meter used for outbox processor counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusOutboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter incremented once per processor pass.
    /// </summary>
    private static readonly Counter<long> PassCounter = Meter.CreateCounter<long>(PassInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each successfully published message.
    /// </summary>
    private static readonly Counter<long> PublishedCounter = Meter.CreateCounter<long>(PublishedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each failed message scheduled for retry.
    /// </summary>
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>(FailedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented for each dead-lettered message.
    /// </summary>
    private static readonly Counter<long> DeadLetteredCounter = Meter.CreateCounter<long>(DeadLetteredInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when the background loop catches an unhandled exception.
    /// </summary>
    private static readonly Counter<long> LoopErrorCounter = Meter.CreateCounter<long>(LoopErrorInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when lease renewal fails during publication.
    /// </summary>
    private static readonly Counter<long> LeaseLostCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorLeaseLostInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist skips a message because the lease was lost.
    /// </summary>
    private static readonly Counter<long> PersistSkippedCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorPersistSkippedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when outbox messages are leased during a pass.
    /// </summary>
    private static readonly Counter<long> LeasesAcquiredCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorLeasesAcquiredInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist rejects an update because the lease was lost.
    /// </summary>
    private static readonly Counter<long> PersistRejectedCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorPersistRejectedInstrumentName);

    /// <summary>
    ///     Gets the counter incremented when terminal persist throws and the processor continues the pass.
    /// </summary>
    private static readonly Counter<long> PersistFailedCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ProcessorPersistFailedInstrumentName);

    /// <summary>
    ///     Gets the histogram recording outbox publication duration in milliseconds.
    /// </summary>
    private static readonly Histogram<double> DispatchDurationHistogram =
        Meter.CreateHistogram<double>(LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName, "ms");

    /// <summary>
    ///     Records that the outbox processor background loop caught an unhandled exception.
    /// </summary>
    public static void RecordLoopError()
    {
        LoopErrorCounter.Add(1);
    }

    /// <summary>
    ///     Records that outbox lease renewal failed and publication was canceled.
    /// </summary>
    public static void RecordLeaseLost()
    {
        LeaseLostCounter.Add(1);
    }

    /// <summary>
    ///     Records that terminal persist skipped a message because the active lease no longer matched.
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
    ///     Records the number of outbox messages leased during one processor pass.
    /// </summary>
    /// <param name="leasedCount">The number of messages leased.</param>
    public static void RecordLeasesAcquired(int leasedCount)
    {
        if (leasedCount > 0)
        {
            LeasesAcquiredCounter.Add(leasedCount);
        }
    }

    /// <summary>
    ///     Records outbox publication duration for one message.
    /// </summary>
    /// <param name="duration">The publication duration.</param>
    public static void RecordDispatchDuration(TimeSpan duration)
    {
        DispatchDurationHistogram.Record(duration.TotalMilliseconds);
    }

    /// <summary>
    ///     Records pass-level metrics after one processor pass completes.
    /// </summary>
    /// <param name="leasedCount">The number of messages leased during the pass.</param>
    /// <param name="publishedCount">The number of messages marked published.</param>
    /// <param name="failedCount">The number of messages marked failed.</param>
    /// <param name="deadLetteredCount">The number of messages moved to dead letter.</param>
    public static void RecordPass(int leasedCount, int publishedCount, int failedCount, int deadLetteredCount)
    {
        PassCounter.Add(1);
        PublishedCounter.Add(publishedCount);
        FailedCounter.Add(failedCount);
        DeadLetteredCounter.Add(deadLetteredCount);
    }
}
