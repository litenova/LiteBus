using System.Diagnostics.Metrics;
using LiteBus.Outbox;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     OpenTelemetry counters for in-dispatch reaction retries after storage conflicts.
/// </summary>
/// <remarks>
///     Reuses the outbox meter, so <c>LiteBus.Outbox.Extensions.OpenTelemetry</c> needs no change.
/// </remarks>
internal static class OutboxReactionRetryTelemetry
{
    /// <summary>
    ///     Gets the meter used for reaction retry counters.
    /// </summary>
    private static readonly Meter Meter = new(LiteBusOutboxTelemetry.MeterName);

    /// <summary>
    ///     Gets the counter for in-dispatch reaction retries scheduled after a storage conflict.
    /// </summary>
    private static readonly Counter<long> RetryCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ReactionRetryInstrumentName);

    /// <summary>
    ///     Gets the counter for dispatches where a storage conflict meets no further in-dispatch retry.
    /// </summary>
    private static readonly Counter<long> ExhaustedCounter =
        Meter.CreateCounter<long>(LiteBusOutboxTelemetry.ReactionRetryExhaustedInstrumentName);

    /// <summary>
    ///     Records that one conflicted reaction publishes again inside the same dispatch.
    /// </summary>
    public static void RecordRetry()
    {
        RetryCounter.Add(1);
    }

    /// <summary>
    ///     Records that one dispatch met a storage conflict it does not retry in dispatch.
    /// </summary>
    public static void RecordExhausted()
    {
        ExhaustedCounter.Add(1);
    }
}
