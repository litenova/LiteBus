using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Commands.Recurring.Schedules.Cron;

/// <summary>
///     A schedule that fires at the occurrences of a cron expression read in each tenant's zone.
/// </summary>
internal sealed class TenantCronSchedule : ITenantSchedule
{
    /// <summary>
    ///     The parsed cron expression occurrences are read from.
    /// </summary>
    private readonly CronExpression _expression;

    /// <summary>
    ///     Resolves the zone one tenant's expression is read in.
    /// </summary>
    private readonly Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> _zoneOf;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantCronSchedule" /> class.
    /// </summary>
    /// <param name="expression">The parsed cron expression occurrences are read from.</param>
    /// <param name="zoneOf">Resolves the zone one tenant's expression is read in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="expression" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="zoneOf" /> is <see langword="null" />.</exception>
    public TenantCronSchedule(
        CronExpression expression,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(zoneOf);

        _expression = expression;
        _zoneOf = zoneOf;
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
        string tenantId,
        DateTimeOffset after,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var resolved = await _zoneOf(tenantId, cancellationToken).ConfigureAwait(false);
        var zone = resolved ?? TimeZoneInfo.Utc;

        return _expression.GetNextOccurrence(after, zone);
    }
}
