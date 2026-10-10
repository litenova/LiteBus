using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines retry behavior shared by inbox and outbox processors.
/// </summary>
/// <remarks>
///     <para>
///         Processors read these values after a handler or dispatcher failure. Stores receive only the final next-visible
///         timestamp on the transitioned envelope, which keeps retry policy outside the storage layer.
///     </para>
///     <para>
///         Choose values according to the side effect being retried. Short delays work for transient local dependencies;
///         external transports and payment systems often need longer caps and idempotent consumers.
///     </para>
/// </remarks>
public sealed record RetryOptions
{
    /// <summary>
    ///     Gets the maximum number of dispatch attempts before a message is dead-lettered.
    /// </summary>
    /// <value>Must be greater than zero.</value>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>
    ///     Gets the delay before the first retry. Subsequent delays scale with <see cref="Backoff" />.
    /// </summary>
    /// <value>Must not be negative.</value>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the upper bound on the calculated delay regardless of backoff and attempt count.
    /// </summary>
    /// <value>Must not be negative.</value>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the retry backoff strategy used to calculate the base delay for each attempt.
    /// </summary>
    /// <value>Must be a defined <see cref="RetryBackoff" /> value.</value>
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;

    /// <summary>
    ///     Gets the explicit ladder of delays used when <see cref="Backoff" /> is <see cref="RetryBackoff.Schedule" />.
    /// </summary>
    /// <value>
    ///     One delay per retry, in order. The first entry is the delay before the first retry. Must be non-empty and
    ///     free of negative entries when <see cref="Backoff" /> is <see cref="RetryBackoff.Schedule" />; ignored for
    ///     every other backoff.
    /// </value>
    /// <remarks>
    ///     <para>
    ///         An attempt past the end of the ladder repeats the last entry, so a ladder shorter than
    ///         <see cref="MaxAttempts" /> settles at its final delay rather than failing or wrapping.
    ///     </para>
    ///     <para>
    ///         <see cref="MaxDelay" /> does not clamp these entries. The ladder is the policy, and a cap that silently
    ///         shortened a declared seven-day tail would defeat the reason for declaring it.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<TimeSpan> Delays { get; init; } = [];

    /// <summary>
    ///     Gets a value indicating whether retry delays include jitter.
    /// </summary>
    /// <value>
    ///     When <see langword="true" />, applies a random ±20% jitter factor to the calculated delay to reduce retry
    ///     storms under concurrent load.
    /// </value>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    ///     Calculates the visibility delay before the next dispatch attempt.
    /// </summary>
    /// <param name="attemptCount">
    ///     The number of attempts already made. Pass the leased envelope attempt count directly; the lease store
    ///     increments it before the processor sees the envelope, so a value of <c>1</c> represents the first attempt.
    /// </param>
    /// <returns>The delay to add to the current clock value before the envelope becomes visible again.</returns>
    public TimeSpan CalculateDelay(int attemptCount)
    {
        if (Backoff == RetryBackoff.Schedule)
        {
            return CalculateScheduledDelay(attemptCount);
        }

        var initialTicks = InitialDelay.Ticks;

        var rawTicks = Backoff == RetryBackoff.Fixed
            ? initialTicks
            : initialTicks * Math.Pow(2, Math.Max(0, attemptCount - 1));

        var delay = TimeSpan.FromTicks((long) Math.Min(rawTicks, MaxDelay.Ticks));

        if (!UseJitter || delay == TimeSpan.Zero)
        {
            return delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long) Math.Min(delay.Ticks * jitterFactor, MaxDelay.Ticks));
    }

    /// <summary>
    ///     Reads the delay for an attempt from the explicit <see cref="Delays" /> ladder.
    /// </summary>
    /// <param name="attemptCount">The number of attempts already made, where <c>1</c> is the first attempt.</param>
    /// <returns>The ladder entry for the attempt, jittered when <see cref="UseJitter" /> is set.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Delays" /> is empty.</exception>
    /// <remarks>
    ///     Attempts past the end of the ladder repeat the last entry. Jitter still applies, because a ladder shared by
    ///     many messages retried at once has the same retry-storm problem a curve does, but it is bounded by the entry
    ///     rather than by <see cref="MaxDelay" />.
    /// </remarks>
    private TimeSpan CalculateScheduledDelay(int attemptCount)
    {
        if (Delays.Count == 0)
        {
            throw new InvalidOperationException(
                "RetryOptions.Backoff is RetryBackoff.Schedule but Delays is empty. Supply one delay per retry, or "
                + "choose RetryBackoff.Fixed or RetryBackoff.Exponential.");
        }

        var index = Math.Clamp(attemptCount - 1, 0, Delays.Count - 1);
        var delay = Delays[index];

        if (!UseJitter || delay <= TimeSpan.Zero)
        {
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long) (delay.Ticks * jitterFactor));
    }

    /// <summary>
    ///     Validates the options before a processor uses them.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A count or delay is outside its allowed range.</exception>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="Backoff" /> is <see cref="RetryBackoff.Schedule" /> and <see cref="Delays" /> is empty.
    /// </exception>
    /// <remarks>
    ///     Processors call this at compose time so a misconfigured ladder fails at boot rather than on the first
    ///     failure, which is the one moment nobody is watching the retry policy.
    /// </remarks>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(InitialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDelay, TimeSpan.Zero);

        if (Backoff != RetryBackoff.Schedule)
        {
            return;
        }

        if (Delays.Count == 0)
        {
            throw new InvalidOperationException(
                "RetryOptions.Backoff is RetryBackoff.Schedule but Delays is empty. Supply one delay per retry, or "
                + "choose RetryBackoff.Fixed or RetryBackoff.Exponential.");
        }

        for (var index = 0; index < Delays.Count; index++)
        {
            if (Delays[index] < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Delays),
                    Delays[index],
                    $"RetryOptions.Delays[{index}] is negative. Every ladder entry must be zero or greater.");
            }
        }
    }
}
