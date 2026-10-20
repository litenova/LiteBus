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
    /// <value>The default is one minute. Must be positive.</value>
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
    ///     The default is five minutes. Must be positive. A failing dependency is usually still failing one interval
    ///     later, so backing off further keeps a broken loop from filling the log at the polling rate.
    /// </value>
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Gets or sets the predicate that recognizes an optimistic-concurrency conflict thrown by a send.
    /// </summary>
    /// <value>
    ///     The default is <see cref="IsConcurrencyConflictByTypeName" />. Set it when the application's persistence
    ///     reports conflicts with an exception type the default does not recognize, or to <see langword="null" /> to treat
    ///     every exception as a failure.
    /// </value>
    /// <remarks>
    ///     Because the loop runs on every host instance, two instances sending the command at the same time can both
    ///     reach the same record, and the one that loses the race gets a conflict for work the other already did. That is
    ///     a normal outcome, not a fault: a send classified as a conflict is logged at Information and the loop waits
    ///     <see cref="Interval" /> rather than <see cref="ErrorBackoff" />.
    /// </remarks>
    public Func<Exception, bool>? IsConcurrencyConflict { get; set; } = IsConcurrencyConflictByTypeName;

    /// <summary>
    ///     Recognizes an optimistic-concurrency conflict by the shape of its exception type name.
    /// </summary>
    /// <param name="exception">The exception a send threw.</param>
    /// <returns>
    ///     <see langword="true" /> when the exception, an inner exception, or an exception aggregated in an
    ///     <see cref="AggregateException" /> has a type name ending in <c>ConcurrencyException</c>.
    /// </returns>
    /// <remarks>
    ///     Matching by name keeps this package free of persistence references while recognizing the conflict types common
    ///     .NET persistence libraries throw, such as <c>DbUpdateConcurrencyException</c> from Entity Framework Core and
    ///     the <c>ConcurrencyException</c> Marten raises through JasperFx.
    /// </remarks>
    public static bool IsConcurrencyConflictByTypeName(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.GetType().Name.EndsWith("ConcurrencyException", StringComparison.Ordinal))
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (IsConcurrencyConflictByTypeName(inner))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null && IsConcurrencyConflictByTypeName(exception.InnerException);
    }

    /// <summary>
    ///     Validates the timing values before the loop starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="Interval" /> or <see cref="ErrorBackoff" /> is zero or negative, or
    ///     <see cref="StartupDelay" /> is negative.
    /// </exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Interval.Ticks);
        ArgumentOutOfRangeException.ThrowIfLessThan(StartupDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ErrorBackoff.Ticks);
    }
}
