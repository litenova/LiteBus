using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Configures how the in-process outbox dispatcher retries a reaction that met a storage conflict.
/// </summary>
/// <remarks>
///     Set through <see cref="OutboxModuleBuilder.UseReactionRetry" />. The values shape the default retry curve
///     and the per-dispatch safety bound; the per-failure decision stays with
///     <see cref="IOutboxReactionRetryPolicy" /> at evaluation time.
/// </remarks>
public sealed class OutboxReactionRetryOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether a reaction that met a storage conflict publishes again inside
    ///     the same dispatch.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> by default. <see langword="false" /> keeps conflicts on the ordinary envelope
    ///     retry schedule with no in-dispatch attempts.
    /// </value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the total in-dispatch attempts per delivery, including the first one.
    /// </summary>
    /// <value>
    ///     Three by default. The dispatcher enforces this bound for every policy, including replacements, so a
    ///     policy that always retries cannot hold a lease forever. Raise it when a custom policy needs more
    ///     attempts.
    /// </value>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    ///     Gets or sets the delay before the first in-dispatch retry. Subsequent delays double per attempt.
    /// </summary>
    /// <value>One hundred milliseconds by default. Must not be negative.</value>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Gets or sets the upper bound on a calculated in-dispatch retry delay.
    /// </summary>
    /// <value>Five seconds by default. Must not be negative.</value>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Gets or sets a value indicating whether in-dispatch retry delays include jitter.
    /// </summary>
    /// <value>
    ///     When <see langword="true" /> (the default), applies a random ±20% jitter factor to the calculated
    ///     delay to reduce retry storms under concurrent load.
    /// </value>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    ///     Throws when the values cannot work together.
    /// </summary>
    /// <exception cref="LiteBusConfigurationException">
    ///     <see cref="MaxAttempts" /> is less than one, or <see cref="InitialDelay" /> or
    ///     <see cref="MaxDelay" /> is negative.
    /// </exception>
    public void Validate()
    {
        if (MaxAttempts < 1)
        {
            throw new LiteBusConfigurationException(
                $"OutboxReactionRetryOptions.MaxAttempts must be at least 1, but it is {MaxAttempts}.");
        }

        if (InitialDelay < TimeSpan.Zero)
        {
            throw new LiteBusConfigurationException(
                $"OutboxReactionRetryOptions.InitialDelay must not be negative, but it is {InitialDelay}.");
        }

        if (MaxDelay < TimeSpan.Zero)
        {
            throw new LiteBusConfigurationException(
                $"OutboxReactionRetryOptions.MaxDelay must not be negative, but it is {MaxDelay}.");
        }
    }
}
