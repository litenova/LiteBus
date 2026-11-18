using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace LiteBus.Outbox.ImmediateDispatch;

/// <summary>
///     OpenTelemetry counters and histogram for immediate dispatch after a commit.
/// </summary>
/// <remarks>
///     Reuses the outbox meter, so <c>LiteBus.Outbox.Extensions.OpenTelemetry</c> needs no change.
/// </remarks>
internal static class OutboxImmediateDispatchTelemetry
{
    /// <summary>
    ///     Gets the outcome value recorded when immediate dispatch publishes an envelope.
    /// </summary>
    private const string PublishedOutcome = "published";

    /// <summary>
    ///     Gets the outcome value recorded when immediate dispatch fails an envelope, including dead-lettering.
    /// </summary>
    private const string FailedOutcome = "failed";

    /// <summary>
    ///     Gets the outcome value recorded when an envelope staged for immediate dispatch cannot be claimed.
    /// </summary>
    private const string SkippedOutcome = "skipped";

    /// <summary>
    ///     Gets the outcome value recorded when an envelope is released because the timeout expired.
    /// </summary>
    private const string TimedOutOutcome = "timed_out";

    /// <summary>
    ///     Gets the meter used for immediate dispatch counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusOutboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter for envelopes handled by immediate dispatch, tagged by outcome.
    /// </summary>
    private static readonly Counter<long> CountCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ImmediateDispatchCountInstrumentName);

    /// <summary>
    ///     Gets the histogram for the time immediate dispatch took after one commit, in seconds.
    /// </summary>
    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>(LiteBusOutboxTelemetry.ImmediateDispatchDurationInstrumentName, "s");

    /// <summary>
    ///     Gets the counter for immediate-addressed envelopes staged with scheduled visibility, tagged by contract.
    /// </summary>
    private static readonly Counter<long> ScheduledSkippedCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ImmediateDispatchSkippedScheduledInstrumentName);

    /// <summary>
    ///     Records that immediate dispatch published one envelope.
    /// </summary>
    public static void RecordPublished()
    {
        CountCounter.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.ImmediateDispatchOutcomeAttributeName, PublishedOutcome));
    }

    /// <summary>
    ///     Records that immediate dispatch failed one envelope.
    /// </summary>
    public static void RecordFailed()
    {
        CountCounter.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.ImmediateDispatchOutcomeAttributeName, FailedOutcome));
    }

    /// <summary>
    ///     Records that one staged envelope was skipped because it could not be claimed.
    /// </summary>
    public static void RecordSkipped()
    {
        CountCounter.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.ImmediateDispatchOutcomeAttributeName, SkippedOutcome));
    }

    /// <summary>
    ///     Records that one envelope was released because the timeout expired.
    /// </summary>
    public static void RecordTimedOut()
    {
        CountCounter.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.ImmediateDispatchOutcomeAttributeName, TimedOutOutcome));
    }

    /// <summary>
    ///     Records how long immediate dispatch took after one commit.
    /// </summary>
    /// <param name="duration">The total dispatch time for the commit.</param>
    public static void RecordDuration(TimeSpan duration)
    {
        DurationHistogram.Record(duration.TotalSeconds);
    }

    /// <summary>
    ///     Records that one immediate-addressed envelope was staged with scheduled visibility.
    /// </summary>
    /// <param name="contractName">The stable contract name of the staged envelope.</param>
    public static void RecordScheduledSkipped(string contractName)
    {
        ScheduledSkippedCounter.Add(
            1,
            new KeyValuePair<string, object?>(LiteBusOutboxTelemetry.ImmediateDispatchContractAttributeName, contractName));
    }
}
