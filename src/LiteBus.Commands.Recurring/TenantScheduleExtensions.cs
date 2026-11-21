using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Scheduling;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Adapts shared tenant schedules to the recurring tenant schedule port.
/// </summary>
public static class TenantScheduleExtensions
{
    /// <summary>
    ///     Exposes a shared tenant schedule through <see cref="ITenantRecurringSchedule" />.
    /// </summary>
    /// <param name="schedule">The shared tenant schedule occurrence computation delegates to.</param>
    /// <returns>The recurring tenant schedule.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schedule" /> is <see langword="null" />.</exception>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddCommands(commands => commands
    ///     .AddRecurringForEachTenant<CloseDayCommand, StoreTenantSource>(
    ///         tenant => new CloseDayCommand { TenantId = tenant },
    ///         host => host.TenantSchedule = RecurringSchedules
    ///             .DailyAt(new TimeOnly(0, 0), TenantZones.FromMetadata(provider))
    ///             .AsRecurringSchedule()));
    /// ]]></code>
    /// </example>
    public static ITenantRecurringSchedule AsRecurringSchedule(this ITenantSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return new TenantScheduleAdapter(schedule);
    }

    /// <summary>
    ///     Exposes a shared tenant schedule through the recurring tenant schedule port.
    /// </summary>
    private sealed class TenantScheduleAdapter : ITenantRecurringSchedule
    {
        /// <summary>
        ///     The shared tenant schedule occurrence computation delegates to.
        /// </summary>
        private readonly ITenantSchedule _schedule;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TenantScheduleAdapter" /> class.
        /// </summary>
        /// <param name="schedule">The shared tenant schedule occurrence computation delegates to.</param>
        public TenantScheduleAdapter(ITenantSchedule schedule)
        {
            ArgumentNullException.ThrowIfNull(schedule);

            _schedule = schedule;
        }

        /// <inheritdoc />
        public ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
            string tenantId,
            DateTimeOffset after,
            CancellationToken cancellationToken)
        {
            return _schedule.GetNextOccurrenceAsync(tenantId, after, cancellationToken);
        }
    }
}
