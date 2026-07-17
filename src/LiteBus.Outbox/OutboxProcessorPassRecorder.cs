using System;
using System.Diagnostics;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox;

/// <summary>
///     Records outbox processor pass telemetry and log entries from accumulated outcomes.
/// </summary>
internal static class OutboxProcessorPassRecorder
{
    /// <summary>
    ///     Builds the pass result and records telemetry for a completed outbox pass.
    /// </summary>
    /// <param name="accumulator">The pass accumulator that collected outcomes.</param>
    /// <param name="leasedCount">The number of messages leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <param name="passActivity">The optional OpenTelemetry activity for the pass.</param>
    /// <param name="logger">The logger used for pass completion diagnostics.</param>
    /// <returns>The processor pass result.</returns>
    public static ProcessorPassResult FinalizePass(
        ProcessorPassAccumulator<OutboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        var result = accumulator.ToResult(leasedCount, elapsed);

        passActivity?.SetTag("litebus.outbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.outbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.outbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.outbox.dead_lettered_count", result.DeadLetteredCount);

        OutboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        OutboxProcessorLogMessages.PassCompleted(
            logger,
            result.ElapsedTime.TotalMilliseconds,
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }

    /// <summary>
    ///     Builds the pass result and records telemetry for a completed pipelined outbox pass.
    /// </summary>
    /// <param name="accumulator">The concurrent pass accumulator that collected outcomes.</param>
    /// <param name="leasedCount">The number of messages leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <param name="passActivity">The optional OpenTelemetry activity for the pass.</param>
    /// <param name="logger">The logger used for pass completion diagnostics.</param>
    /// <returns>The processor pass result.</returns>
    public static ProcessorPassResult FinalizePass(
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        var result = accumulator.ToResult(leasedCount, elapsed);

        passActivity?.SetTag("litebus.outbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.outbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.outbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.outbox.dead_lettered_count", result.DeadLetteredCount);

        OutboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        OutboxProcessorLogMessages.PassCompleted(
            logger,
            result.ElapsedTime.TotalMilliseconds,
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }
}
