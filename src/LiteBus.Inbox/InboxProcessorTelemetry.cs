using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     OpenTelemetry activity and metric instrumentation for inbox processing.
/// </summary>
internal static class InboxProcessorTelemetry
{
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
    private static readonly Counter<long> PassCounter = Meter.CreateCounter<long>("litebus.inbox.processor.passes");

    /// <summary>
    ///     Gets the counter incremented for each successfully completed envelope.
    /// </summary>
    private static readonly Counter<long> SucceededCounter = Meter.CreateCounter<long>("litebus.inbox.processor.succeeded");

    /// <summary>
    ///     Gets the counter incremented for each failed envelope scheduled for retry.
    /// </summary>
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("litebus.inbox.processor.failed");

    /// <summary>
    ///     Gets the counter incremented for each dead-lettered envelope.
    /// </summary>
    private static readonly Counter<long> DeadLetteredCounter = Meter.CreateCounter<long>("litebus.inbox.processor.dead_lettered");

    /// <summary>
    ///     Gets the counter incremented when the background loop catches an unhandled exception.
    /// </summary>
    private static readonly Counter<long> LoopErrorCounter = Meter.CreateCounter<long>("litebus.inbox.processor.loop_errors");

    /// <summary>
    ///     Records that the inbox processor background loop caught an unhandled exception.
    /// </summary>
    public static void RecordLoopError()
    {
        LoopErrorCounter.Add(1);
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
