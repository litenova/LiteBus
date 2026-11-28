using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     The answer one outbox reaction retry evaluation gives: wait and publish again, or stop.
/// </summary>
/// <remarks>
///     Returned by <see cref="IOutboxReactionRetryPolicy.DecideAsync" />. A <see cref="Retry" /> republishes the
///     reaction as a new root mediation after <see cref="Retry.Delay" />; a <see cref="GiveUp" /> ends the
///     in-dispatch retries and the envelope follows the ordinary processor retry schedule.
/// </remarks>
public abstract record OutboxReactionRetryDecision
{
    /// <summary>
    ///     Republishes the reaction after a delay, as a new root mediation with the same cause.
    /// </summary>
    /// <param name="Delay">The delay before the next in-dispatch attempt.</param>
    public sealed record Retry(TimeSpan Delay) : OutboxReactionRetryDecision;

    /// <summary>
    ///     Stops the in-dispatch retries; the envelope follows the ordinary processor retry schedule.
    /// </summary>
    public sealed record GiveUp : OutboxReactionRetryDecision;
}
