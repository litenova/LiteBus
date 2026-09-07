namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the retry backoff strategy used by inbox and outbox processors.
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
    Exponential = 1,

    /// <summary>
    ///     Each retry takes its delay from the explicit ladder in <see cref="RetryOptions.Delays" />.
    /// </summary>
    /// <remarks>
    ///     Use this when the retry window is a product decision rather than a load-shedding one. A ladder can start
    ///     inside a minute and still reach a week, which no exponential curve under a single cap can express.
    /// </remarks>
    Schedule = 2
}
