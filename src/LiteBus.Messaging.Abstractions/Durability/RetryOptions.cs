using System;

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
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the upper bound on the calculated delay regardless of backoff and attempt count.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the retry backoff strategy used to calculate the base delay for each attempt.
    /// </summary>
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;

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
        var initialTicks = InitialDelay.Ticks;
        var rawTicks = Backoff == RetryBackoff.Fixed
            ? initialTicks
            : initialTicks * Math.Pow(2, Math.Max(0, attemptCount - 1));

        var delay = TimeSpan.FromTicks((long)Math.Min(rawTicks, MaxDelay.Ticks));

        if (!UseJitter || delay == TimeSpan.Zero)
        {
            return delay;
        }

        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromTicks((long)Math.Min(delay.Ticks * jitterFactor, MaxDelay.Ticks));
    }
}
