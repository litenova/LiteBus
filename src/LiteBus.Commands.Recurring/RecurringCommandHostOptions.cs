using System;
using System.Data.Common;
using System.Reflection;

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
///         <strong>By default the loop runs on every host instance.</strong> A deployment of three replicas sends the
///         command three times per interval, concurrently. That is correct for a command that drains a queue-like
///         table by leasing rows, because the store arbitrates and the instances share the work. It is wrong for a
///         command that assumes it is alone. Make the handler safe to run concurrently with itself, set
///         <see cref="RequireLease" /> so the pass runs on at most one instance at a time, or do not use this.
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
    ///     The default is <see cref="IsConcurrencyOrDatabaseConflict" />. Set it when the application's persistence
    ///     reports conflicts in a way the default does not recognize, or to <see langword="null" /> to treat
    ///     every exception as a failure.
    /// </value>
    /// <remarks>
    ///     Because the loop runs on every host instance, two instances sending the command at the same time can both
    ///     reach the same record, and the one that loses the race gets a conflict for work the other already did. That is
    ///     a normal outcome, not a fault: a send classified as a conflict is logged at Information and the loop waits
    ///     <see cref="Interval" /> rather than <see cref="ErrorBackoff" />.
    /// </remarks>
    public Func<Exception, bool>? IsConcurrencyConflict { get; set; } = IsConcurrencyOrDatabaseConflict;

    /// <summary>
    ///     Gets or sets a value indicating whether each pass first acquires the named single-flight lease.
    /// </summary>
    /// <value>
    ///     The default is <see langword="false" />. When <see langword="true" />, each pass tries
    ///     <see cref="IRecurringCommandLease" /> before sending: a pass that cannot acquire the lease is skipped and
    ///     logged at Debug (EventId 2105), and the loop waits <see cref="Interval" />. Composition fails when no
    ///     <see cref="IRecurringCommandLease" /> is registered.
    /// </value>
    /// <remarks>
    ///     One acquired lease covers the whole busy period: immediate re-sends driven by
    ///     <see cref="IRecurringWorkReport.HasMoreWork" /> reuse the lease the pass acquired, and it is released before
    ///     the loop waits. Re-acquiring per send would let another replica interleave mid-backlog, which is exactly
    ///     the interleaving single-flight exists to prevent.
    /// </remarks>
    public bool RequireLease { get; set; }

    /// <summary>
    ///     Gets or sets the lease name acquired before each pass when <see cref="RequireLease" /> is
    ///     <see langword="true" />.
    /// </summary>
    /// <value>
    ///     The default is <see langword="null" />, which uses the command type's full name. Set an explicit name when
    ///     two loops must never run at the same time: leases are per name, so loops sharing a name single-flight
    ///     against each other.
    /// </value>
    public string? LeaseName { get; set; }

    /// <summary>
    ///     Recognizes an optimistic-concurrency conflict, or a database deadlock or serialization failure, thrown by a
    ///     send.
    /// </summary>
    /// <param name="exception">The exception a send threw.</param>
    /// <returns>
    ///     <see langword="true" /> when <see cref="IsConcurrencyConflictByTypeName" /> recognizes the exception, or when a
    ///     <see cref="DbException" /> in its inner or aggregate chain reports a lost race; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Two instances running the same pass inside one database transaction can also collide in the database
    ///         itself: one of them is chosen as a deadlock victim or fails serialization, which is the same lost race as
    ///         an optimistic-concurrency conflict. A handler that writes through raw SQL or a plain
    ///         <c>DbUpdateException</c> reports it only that way.
    ///     </para>
    ///     <para>
    ///         The decision reads the structured error the driver attaches: SQLSTATE <c>40001</c> (serialization
    ///         failure, which MySQL also reports for a deadlock) or <c>40P01</c> (PostgreSQL deadlock) through
    ///         <see cref="DbException.SqlState" />; error number <c>1205</c> (SQL Server deadlock victim, MySQL lock wait
    ///         timeout) or <c>1213</c> (MySQL deadlock) on a public <c>Number</c> property; or result code <c>5</c>
    ///         (busy) or <c>6</c> (locked) on a public <c>SqliteErrorCode</c> property. The two properties are read by
    ///         name, which keeps this package free of database driver references.
    ///     </para>
    /// </remarks>
    public static bool IsConcurrencyOrDatabaseConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return IsConcurrencyConflictByTypeName(exception) || IsDatabaseConflict(exception);
    }

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
    ///     Determines whether a database error in the exception's inner or aggregate chain reports a lost race.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><see langword="true" /> when a driver error reports a deadlock, serialization failure, or lock contention.</returns>
    private static bool IsDatabaseConflict(Exception exception)
    {
        if (exception is DbException databaseError && IsDatabaseConflictCode(databaseError))
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (IsDatabaseConflict(inner))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null && IsDatabaseConflict(exception.InnerException);
    }

    /// <summary>
    ///     Determines whether one driver error carries a code that reports a lost race.
    /// </summary>
    /// <param name="databaseError">The driver error.</param>
    /// <returns><see langword="true" /> for a deadlock, serialization failure, or lock contention code.</returns>
    private static bool IsDatabaseConflictCode(DbException databaseError)
    {
        if (databaseError.SqlState is "40001" or "40P01")
        {
            return true;
        }

        if (TryReadInteger(databaseError, "Number", out var number))
        {
            return number is 1205 or 1213;
        }

        return TryReadInteger(databaseError, "SqliteErrorCode", out var sqliteCode) && sqliteCode is 5 or 6;
    }

    /// <summary>
    ///     Reads a public integer property from a driver exception.
    /// </summary>
    /// <param name="databaseError">The driver error.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value when the property exists.</param>
    /// <returns><see langword="true" /> when the exception type has the property; otherwise, <see langword="false" />.</returns>
    private static bool TryReadInteger(DbException databaseError, string propertyName, out int value)
    {
        if (databaseError.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var type } property
            && type == typeof(int)
            && property.GetValue(databaseError) is int number)
        {
            value = number;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    ///     Validates the timing values and the lease name before the loop starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="Interval" /> or <see cref="ErrorBackoff" /> is zero or negative, or
    ///     <see cref="StartupDelay" /> is negative.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="LeaseName" /> is set to an empty or whitespace value.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Interval.Ticks);
        ArgumentOutOfRangeException.ThrowIfLessThan(StartupDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ErrorBackoff.Ticks);

        if (LeaseName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(LeaseName);
        }
    }
}
