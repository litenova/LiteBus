using System;
using System.Diagnostics;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     Records inbox processor pass telemetry and log entries from accumulated outcomes.
/// </summary>
internal static class InboxProcessorPassRecorder
{
    /// <summary>
    ///     Builds the pass result and records telemetry for a completed inbox pass.
    /// </summary>
    /// <param name="accumulator">The pass accumulator that collected outcomes.</param>
    /// <param name="leasedCount">The number of envelopes leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <param name="passActivity">The optional OpenTelemetry activity for the pass.</param>
    /// <param name="logger">The logger used for pass completion diagnostics.</param>
    /// <returns>The processor pass result.</returns>
    public static ProcessorPassResult FinalizePass(
        ProcessorPassAccumulator<InboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        var result = accumulator.ToResult(leasedCount, elapsed);

        passActivity?.SetTag("litebus.inbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.inbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.inbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.inbox.dead_lettered_count", result.DeadLetteredCount);

        InboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        InboxProcessorLogMessages.PassCompleted(
            logger,
            result.ElapsedTime.TotalMilliseconds,
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }

    /// <summary>
    ///     Builds the pass result and records telemetry for a completed pipelined inbox pass.
    /// </summary>
    /// <param name="accumulator">The concurrent pass accumulator that collected outcomes.</param>
    /// <param name="leasedCount">The number of envelopes leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <param name="passActivity">The optional OpenTelemetry activity for the pass.</param>
    /// <param name="logger">The logger used for pass completion diagnostics.</param>
    /// <returns>The processor pass result.</returns>
    public static ProcessorPassResult FinalizePass(
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        var result = accumulator.ToResult(leasedCount, elapsed);

        passActivity?.SetTag("litebus.inbox.leased_count", result.LeasedCount);
        passActivity?.SetTag("litebus.inbox.succeeded_count", result.SucceededCount);
        passActivity?.SetTag("litebus.inbox.failed_count", result.FailedCount);
        passActivity?.SetTag("litebus.inbox.dead_lettered_count", result.DeadLetteredCount);

        InboxProcessorTelemetry.RecordPass(
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        InboxProcessorLogMessages.PassCompleted(
            logger,
            result.ElapsedTime.TotalMilliseconds,
            result.LeasedCount,
            result.SucceededCount,
            result.FailedCount,
            result.DeadLetteredCount);

        return result;
    }
}
