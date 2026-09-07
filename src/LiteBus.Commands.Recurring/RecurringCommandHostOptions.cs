using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Controls the lifecycle of one recurring command loop.
/// </summary>
/// <remarks>
///     <para>
///         This is a poll loop, not a scheduler. It has no cron expression, no wall-clock alignment, no missed-run
///         catch-up, and no notion of a run that was due while the host was down. If the work must happen at a
///         particular time rather than regularly, use a scheduler.
///     </para>
///     <para>
///         <strong>The loop runs on every host instance.</strong> LiteBus has no leader election, so a deployment of
///         three replicas sends the command three times per interval, concurrently. That is correct for a command that
///         drains a queue-like table by leasing rows, because the store arbitrates and the instances share the work.
///         It is wrong for a command that assumes it is alone. Make the handler safe to run concurrently with itself,
///         or do not use this.
///     </para>
/// </remarks>
public sealed class RecurringCommandHostOptions
{
    /// <summary>
    ///     Gets or sets a value indicating whether the loop runs.
    /// </summary>
    /// <value>The default is <see langword="true" />.</value>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets the delay between sends when the previous send reported no more work.
    /// </summary>
    /// <value>The default is one minute. Must not be negative.</value>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Gets or sets the delay after host start before the first send.
    /// </summary>
    /// <value>
    ///     The default is <see cref="TimeSpan.Zero" />. Stagger this across loops that share a database so a host
    ///     start does not send every recurring command at once.
    /// </value>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Gets or sets the delay applied after a send throws, instead of <see cref="Interval" />.
    /// </summary>
    /// <value>
    ///     The default is five minutes. A failing dependency is usually still failing one interval later, so backing
    ///     off further keeps a broken loop from filling the log at the polling rate.
    /// </value>
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Validates the timing values before the loop starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A delay is negative.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Interval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(StartupDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(ErrorBackoff, TimeSpan.Zero);
    }
}
