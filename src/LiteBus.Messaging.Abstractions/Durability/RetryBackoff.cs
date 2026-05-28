namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the retry backoff strategy used by durable processors.
/// </summary>
public enum RetryBackoff
{
    /// <summary>
    ///     Each retry uses the same delay before jitter is applied.
    /// </summary>
    Fixed = 0,

    /// <summary>
    ///     Each retry increases the delay exponentially before the maximum delay cap and jitter are applied.
    /// </summary>
    Exponential = 1
}