using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines retry behavior shared by inbox and outbox processors.
/// </summary>
/// <remarks>
///     <para>
///         Processors read these values after a handler or dispatcher failure. Stores receive only the final next-visible
///         timestamp, which keeps retry policy outside the storage layer.
///     </para>
///     <para>
///         Choose values according to the side effect being retried. Short delays work for transient local dependencies;
///         external transports and payment systems often need longer caps and idempotent consumers.
///     </para>
/// </remarks>
public sealed record RetryOptions
{
    /// <summary>
    ///     Gets the maximum number of attempts before dead-lettering. The leasing operation increments attempt count
    ///     before execution or dispatch, so a value of 5 allows five observed attempts.
    /// </summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>
    ///     Gets the initial retry delay used after the first failed attempt.
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets the maximum retry delay. Exponential backoff and jitter are capped at this value.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets the retry backoff strategy used to calculate the base delay for each attempt.
    /// </summary>
    public RetryBackoff Backoff { get; init; } = RetryBackoff.Exponential;

    /// <summary>
    ///     Gets a value that indicates whether retry delays include jitter. Jitter reduces repeated collisions when many
    ///     messages fail at the same time.
    /// </summary>
    public bool UseJitter { get; init; } = true;
}