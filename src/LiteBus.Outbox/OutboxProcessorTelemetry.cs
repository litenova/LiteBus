using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace LiteBus.Outbox;

/// <summary>
///     OpenTelemetry activity and metric instrumentation for outbox processing.
/// </summary>
internal static class OutboxProcessorTelemetry
{
    /// <summary>
    ///     Gets the activity source used for outbox processor spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("LiteBus.Outbox");

    /// <summary>
    ///     Gets the meter used for outbox processor counters.
    /// </summary>
    private static readonly Meter Meter = new("LiteBus.Outbox");

    /// <summary>
    ///     Gets the counter incremented once per processor pass.
    /// </summary>
    private static readonly Counter<long> PassCounter = Meter.CreateCounter<long>("litebus.outbox.processor.passes");

    /// <summary>
    ///     Gets the counter incremented for each successfully published message.
    /// </summary>
    private static readonly Counter<long> PublishedCounter = Meter.CreateCounter<long>("litebus.outbox.processor.published");

    /// <summary>
    ///     Gets the counter incremented for each failed message scheduled for retry.
    /// </summary>
    private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("litebus.outbox.processor.failed");

    /// <summary>
    ///     Gets the counter incremented for each dead-lettered message.
    /// </summary>
    private static readonly Counter<long> DeadLetteredCounter = Meter.CreateCounter<long>("litebus.outbox.processor.dead_lettered");

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
