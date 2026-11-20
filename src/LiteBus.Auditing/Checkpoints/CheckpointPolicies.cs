using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Scheduling;
using LiteBus.Runtime.Scheduling;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     Builds the checkpoint policies an audit chain cuts checkpoints with.
/// </summary>
/// <remarks>
///     <para>
///         The policies are the replaceable <see cref="ICheckpointPolicy" /> port with LiteBus defaults: a chain asks
///         its policy after every append whether a checkpoint is due, and asks a time-based policy on a schedule as
///         well, so a quiet segment still gets its checkpoint. An application that checkpoints on its own rhythm, such
///         as a weekly close, passes <see cref="Never" /> and cuts explicitly instead.
///     </para>
///     <para>
///         Every factory takes an optional label, stored on the checkpoint a due cut produces and passed to each
///         <see cref="ICheckpointSigner" /> and <see cref="ICheckpointPublisher" />, so each can switch on it. The
///         calendar factories default the label to their rhythm.
///     </para>
/// </remarks>
public static class CheckpointPolicies
{
    /// <summary>
    ///     Returns a policy that never cuts on its own, so only explicit cuts produce checkpoints.
    /// </summary>
    /// <returns>A policy that is never due.</returns>
    /// <remarks>
    ///     A product that cuts by its own scheduled command uses this, so the chain stays silent between explicit
    ///     cuts instead of fighting a record-count default.
    /// </remarks>
    public static ICheckpointPolicy Never()
    {
        return new NeverCheckpointPolicy();
    }

    /// <summary>
    ///     Returns a policy that is due every given number of appended records.
    /// </summary>
    /// <param name="recordCount">How many records are appended between checkpoints. Must be positive.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>
    ///     A policy that is due once the segment has advanced the given number of sequences past its last checkpoint.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordCount" /> is zero or negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    public static ICheckpointPolicy EveryRecords(int recordCount, string? label = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordCount, 1);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new RecordCountCheckpointPolicy(recordCount, label);
    }

    /// <summary>
    ///     Returns a policy that is due once the given time has passed since the last checkpoint.
    /// </summary>
    /// <param name="interval">How long after the last checkpoint the next one is due. Must be positive.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>
    ///     A time-based policy that is due once the segment holds records and the interval has passed since the last
    ///     checkpoint.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval" /> is zero or negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    public static ICheckpointPolicy Every(TimeSpan interval, string? label = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TimeIntervalCheckpointPolicy(interval, label);
    }

    /// <summary>
    ///     Returns a policy that is due when any of the given policies is due.
    /// </summary>
    /// <param name="policies">The policies to combine. With none, a checkpoint is never due.</param>
    /// <returns>A policy that consults each given policy in order and is due when one of them is.</returns>
    /// <exception cref="ArgumentException"><paramref name="policies" /> contains a <see langword="null" /> entry.</exception>
    /// <remarks>
    ///     A combined policy carries no label: a cut from it stores <see langword="null" />. For labeled rhythms, use
    ///     one schedule policy per chain instead of combining differently labeled policies.
    /// </remarks>
    public static ICheckpointPolicy Any(params ICheckpointPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        foreach (var policy in policies)
        {
            if (policy is null)
            {
                throw new ArgumentException("The checkpoint policies must not contain a null entry.", nameof(policies));
            }
        }

        return new AnyCheckpointPolicy(policies);
    }

    /// <summary>
    ///     Returns a policy that is due when the given schedule reaches its next occurrence.
    /// </summary>
    /// <param name="schedule">The schedule whose occurrences cut checkpoints.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    public static ICheckpointPolicy OnSchedule(ISchedule schedule, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new ScheduleCheckpointPolicy(schedule, label);
    }

    /// <summary>
    ///     Returns a policy that is due when the given per-tenant schedule reaches its next occurrence.
    /// </summary>
    /// <param name="schedule">The schedule whose per-tenant occurrences cut checkpoints.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the tenant schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     Synchronous evaluation always returns <see langword="false" />, because resolving the tenant zone needs
    ///     input and output. Callers evaluate through <see cref="ICheckpointPolicy.IsDueAsync" />.
    /// </remarks>
    public static ICheckpointPolicy OnTenantSchedule(ITenantSchedule schedule, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TenantScheduleCheckpointPolicy(schedule, label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a day at a fixed local time.
    /// </summary>
    /// <param name="time">The local time each checkpoint is due at.</param>
    /// <param name="zone">The zone the local time is read in, or <see langword="null" /> for UTC.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the daily schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    public static ICheckpointPolicy DailyAt(TimeOnly time, TimeZoneInfo? zone = null, string? label = "daily")
    {
        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new ScheduleCheckpointPolicy(Schedules.DailyAt(time, zone), label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a week on a fixed weekday at a fixed local time.
    /// </summary>
    /// <param name="day">The weekday each checkpoint is due on.</param>
    /// <param name="time">The local time each checkpoint is due at.</param>
    /// <param name="zone">The zone the local time is read in, or <see langword="null" /> for UTC.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the weekly schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    public static ICheckpointPolicy WeeklyOn(
        DayOfWeek day,
        TimeOnly time,
        TimeZoneInfo? zone = null,
        string? label = "weekly")
    {
        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new ScheduleCheckpointPolicy(Schedules.WeeklyOn(day, time, zone), label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a day at a fixed local time read in each tenant's own time zone, taken from
    ///     the registered <see cref="ITenantMetadataProvider" /> when the policy decides.
    /// </summary>
    /// <param name="time">The local time each checkpoint is due at, such as midnight.</param>
    /// <param name="label">The label a due cut stores on its checkpoint, or <see langword="null" /> for none.</param>
    /// <returns>A time-based policy that follows each tenant's daily schedule from its last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     <para>
    ///         The tenant is the one the segmenter describes on the segment head. The zone is read each time the policy
    ///         decides, from the <see cref="ITenantMetadataProvider" /> in the scope of that pass, so a tenant that
    ///         changes its zone is followed without a restart. A tenant whose metadata names no zone reads the local
    ///         time as UTC, and a segment with no tenant is cut once, like <see cref="OnTenantSchedule" /> does.
    ///     </para>
    ///     <para>
    ///         A pass with no <see cref="ITenantMetadataProvider" /> available fails with an
    ///         <see cref="AuditConfigurationException" />, which the checkpoint loop logs per segment, instead of
    ///         silently reading every tenant as UTC.
    ///     </para>
    /// </remarks>
    public static ICheckpointPolicy DailyInTenantZone(TimeOnly time, string? label = "daily")
    {
        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TenantMetadataZoneCheckpointPolicy(zoneOf => Schedules.DailyAt(time, zoneOf), label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a week at a fixed local time read in each tenant's own time zone, taken from
    ///     the registered <see cref="ITenantMetadataProvider" /> when the policy decides.
    /// </summary>
    /// <param name="day">The weekday each checkpoint is due on.</param>
    /// <param name="time">The local time each checkpoint is due at.</param>
    /// <param name="label">The label a due cut stores on its checkpoint, or <see langword="null" /> for none.</param>
    /// <returns>A time-based policy that follows each tenant's weekly schedule from its last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     The zone is read as <see cref="DailyInTenantZone" /> reads it.
    /// </remarks>
    public static ICheckpointPolicy WeeklyInTenantZone(DayOfWeek day, TimeOnly time, string? label = "weekly")
    {
        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TenantMetadataZoneCheckpointPolicy(zoneOf => Schedules.WeeklyOn(day, time, zoneOf), label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a day at a fixed local time read in each tenant zone.
    /// </summary>
    /// <param name="time">The local time each checkpoint is due at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant local time is read in.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the per-tenant daily schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     A tenant whose zone resolves to <see langword="null" /> reads the local time as UTC.
    /// </remarks>
    public static ICheckpointPolicy DailyAt(
        TimeOnly time,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf,
        string? label = "daily")
    {
        ArgumentNullException.ThrowIfNull(zoneOf);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TenantScheduleCheckpointPolicy(Schedules.DailyAt(time, zoneOf), label);
    }

    /// <summary>
    ///     Returns a policy that cuts once a week at a fixed local time read in each tenant zone.
    /// </summary>
    /// <param name="day">The weekday each checkpoint is due on.</param>
    /// <param name="time">The local time each checkpoint is due at.</param>
    /// <param name="zoneOf">Resolves the zone one tenant local time is read in.</param>
    /// <param name="label">
    ///     The label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </param>
    /// <returns>A time-based policy that follows the per-tenant weekly schedule from the last checkpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="day" /> is not a defined weekday.</exception>
    /// <exception cref="ArgumentException"><paramref name="label" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     A tenant whose zone resolves to <see langword="null" /> reads the local time as UTC.
    /// </remarks>
    public static ICheckpointPolicy WeeklyOn(
        DayOfWeek day,
        TimeOnly time,
        Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> zoneOf,
        string? label = "weekly")
    {
        ArgumentNullException.ThrowIfNull(zoneOf);

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        return new TenantScheduleCheckpointPolicy(Schedules.WeeklyOn(day, time, zoneOf), label);
    }

    /// <summary>
    ///     Never cuts on its own, so only explicit cuts produce checkpoints.
    /// </summary>
    private sealed class NeverCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <inheritdoc />
        public string? Label => null;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return false;
        }

        /// <inheritdoc />
        public ValueTask<bool> IsDueAsync(AuditCheckpointCandidate candidate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    ///     Cuts a checkpoint every fixed number of appended records.
    /// </summary>
    private sealed class RecordCountCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <summary>
        ///     How many sequences past the last checkpoint make a checkpoint due. Fixed for the life of the policy.
        /// </summary>
        private readonly int _recordCount;

        /// <summary>
        ///     The label a due cut stores, or <see langword="null" /> for none. Fixed for the life of the policy.
        /// </summary>
        private readonly string? _label;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordCountCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="recordCount">How many records are appended between checkpoints.</param>
        /// <param name="label">The label a due cut stores, or <see langword="null" /> for none.</param>
        public RecordCountCheckpointPolicy(int recordCount, string? label)
        {
            _recordCount = recordCount;
            _label = label;
        }

        /// <inheritdoc />
        public string? Label => _label;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return candidate.Sequence - (candidate.LastCheckpointSequence ?? 0) >= _recordCount;
        }
    }

    /// <summary>
    ///     Cuts a checkpoint once a fixed time has passed since the last checkpoint.
    /// </summary>
    private sealed class TimeIntervalCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <summary>
        ///     How long after the last checkpoint the next one is due. Fixed for the life of the policy.
        /// </summary>
        private readonly TimeSpan _interval;

        /// <summary>
        ///     The label a due cut stores, or <see langword="null" /> for none. Fixed for the life of the policy.
        /// </summary>
        private readonly string? _label;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TimeIntervalCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="interval">How long after the last checkpoint the next one is due.</param>
        /// <param name="label">The label a due cut stores, or <see langword="null" /> for none.</param>
        public TimeIntervalCheckpointPolicy(TimeSpan interval, string? label)
        {
            _interval = interval;
            _label = label;
        }

        /// <inheritdoc />
        public string? Label => _label;

        /// <inheritdoc />
        public bool IsTimeBased => true;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            if (candidate.Count <= 0)
            {
                return false;
            }

            if (candidate.LastCheckpointAt is null)
            {
                return true;
            }

            return candidate.Now - candidate.LastCheckpointAt.Value >= _interval;
        }
    }

    /// <summary>
    ///     Combines checkpoint policies so that any one of them can cut a checkpoint.
    /// </summary>
    private sealed class AnyCheckpointPolicy : ICheckpointPolicy
    {
        /// <summary>
        ///     The policies consulted in order. Copied at construction and never mutated afterwards.
        /// </summary>
        private readonly ICheckpointPolicy[] _policies;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnyCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="policies">The policies to combine.</param>
        public AnyCheckpointPolicy(ICheckpointPolicy[] policies)
        {
            _policies = [.. policies];
        }

        /// <inheritdoc />
        public bool IsTimeBased
        {
            get
            {
                foreach (var policy in _policies)
                {
                    if (policy.IsTimeBased)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            foreach (var policy in _policies)
            {
                if (policy.IsDue(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public async ValueTask<bool> IsDueAsync(AuditCheckpointCandidate candidate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            foreach (var policy in _policies)
            {
                if (await policy.IsDueAsync(candidate, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    ///     Cuts a checkpoint when a shared schedule reaches its next occurrence.
    /// </summary>
    private sealed class ScheduleCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <summary>
        ///     The schedule whose occurrences cut checkpoints. Held for the life of the policy.
        /// </summary>
        private readonly ISchedule _schedule;

        /// <summary>
        ///     The label a due cut stores, or <see langword="null" /> for none. Fixed for the life of the policy.
        /// </summary>
        private readonly string? _label;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScheduleCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="schedule">The schedule whose occurrences cut checkpoints.</param>
        /// <param name="label">The label a due cut stores, or <see langword="null" /> for none.</param>
        public ScheduleCheckpointPolicy(ISchedule schedule, string? label)
        {
            _schedule = schedule;
            _label = label;
        }

        /// <inheritdoc />
        public string? Label => _label;

        /// <inheritdoc />
        public bool IsTimeBased => true;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return Evaluate(candidate);
        }

        /// <summary>
        ///     Decides whether the schedule passed an occurrence since the last checkpoint.
        /// </summary>
        /// <param name="candidate">The current state of the segment.</param>
        /// <returns><see langword="true" /> when a checkpoint is due now.</returns>
        /// <remarks>
        ///     The anchor is the last checkpoint, or the segment opening when none was cut yet. With neither anchor
        ///     the segment holds records but no calendar start, so the first checkpoint is due immediately.
        /// </remarks>
        private bool Evaluate(AuditCheckpointCandidate candidate)
        {
            if (candidate.Count <= 0)
            {
                return false;
            }

            var anchor = candidate.LastCheckpointAt ?? candidate.SegmentInfo?.OpenedAt;

            if (anchor is null)
            {
                return true;
            }

            var next = _schedule.GetNextOccurrence(anchor.Value);

            if (next is null)
            {
                return false;
            }

            return candidate.Now >= next.Value;
        }
    }

    /// <summary>
    ///     Cuts a checkpoint when a per-tenant schedule reaches its next occurrence in the segment tenant zone.
    /// </summary>
    private sealed class TenantScheduleCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <summary>
        ///     The schedule whose per-tenant occurrences cut checkpoints. Held for the life of the policy.
        /// </summary>
        private readonly ITenantSchedule _schedule;

        /// <summary>
        ///     The label a due cut stores, or <see langword="null" /> for none. Fixed for the life of the policy.
        /// </summary>
        private readonly string? _label;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TenantScheduleCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="schedule">The schedule whose per-tenant occurrences cut checkpoints.</param>
        /// <param name="label">The label a due cut stores, or <see langword="null" /> for none.</param>
        public TenantScheduleCheckpointPolicy(ITenantSchedule schedule, string? label)
        {
            _schedule = schedule;
            _label = label;
        }

        /// <inheritdoc />
        public string? Label => _label;

        /// <inheritdoc />
        public bool IsTimeBased => true;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return false;
        }

        /// <inheritdoc />
        public async ValueTask<bool> IsDueAsync(AuditCheckpointCandidate candidate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            if (candidate.Count <= 0)
            {
                return false;
            }

            var tenantId = candidate.SegmentInfo?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return candidate.LastCheckpointAt is null;
            }

            var anchor = candidate.LastCheckpointAt ?? candidate.SegmentInfo!.OpenedAt;
            var next = await _schedule.GetNextOccurrenceAsync(tenantId, anchor, cancellationToken).ConfigureAwait(false);

            if (next is null)
            {
                return false;
            }

            return candidate.Now >= next.Value;
        }
    }
    /// <summary>
    ///     A per-tenant calendar policy whose zones come from the tenant metadata provider of the deciding pass.
    /// </summary>
    private sealed class TenantMetadataZoneCheckpointPolicy : ILabeledCheckpointPolicy
    {
        /// <summary>
        ///     Builds the tenant schedule from a zone resolver bound to the pass's metadata provider.
        /// </summary>
        private readonly Func<Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>, ITenantSchedule> _schedule;

        /// <summary>
        ///     The label a due cut stores, or <see langword="null" />.
        /// </summary>
        private readonly string? _label;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TenantMetadataZoneCheckpointPolicy" /> class.
        /// </summary>
        /// <param name="schedule">Builds the tenant schedule from a zone resolver.</param>
        /// <param name="label">The label a due cut stores, or <see langword="null" />.</param>
        public TenantMetadataZoneCheckpointPolicy(
            Func<Func<string, CancellationToken, ValueTask<TimeZoneInfo?>>, ITenantSchedule> schedule,
            string? label)
        {
            _schedule = schedule;
            _label = label;
        }

        /// <inheritdoc />
        public string? Label => _label;

        /// <inheritdoc />
        public bool IsTimeBased => true;

        /// <inheritdoc />
        public bool IsDue(AuditCheckpointCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            return false;
        }

        /// <inheritdoc />
        /// <exception cref="AuditConfigurationException">No tenant metadata provider is available to the pass.</exception>
        public ValueTask<bool> IsDueAsync(AuditCheckpointCandidate candidate, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(candidate);

            var provider = candidate.Services?.GetService(typeof(ITenantMetadataProvider)) as ITenantMetadataProvider
                           ?? throw new AuditConfigurationException(
                               $"The checkpoint policy for segment '{candidate.Segment}' reads each tenant's time zone "
                               + "from ITenantMetadataProvider, but none is available to the checkpoint pass. Register a "
                               + "tenant metadata provider, or use DailyAt with a fixed zone.");

            var schedule = _schedule((tenantId, token) => ZoneOfAsync(provider, tenantId, token));

            return new TenantScheduleCheckpointPolicy(schedule, _label).IsDueAsync(candidate, cancellationToken);
        }

        /// <summary>
        ///     Reads a tenant's zone from its metadata.
        /// </summary>
        /// <param name="provider">The tenant metadata provider of the pass.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">The token used to cancel the read.</param>
        /// <returns>The tenant's zone, or <see langword="null" /> when its metadata names none.</returns>
        private static async ValueTask<TimeZoneInfo?> ZoneOfAsync(
            ITenantMetadataProvider provider,
            string tenantId,
            CancellationToken cancellationToken)
        {
            var found = await provider.GetManyAsync([tenantId], cancellationToken).ConfigureAwait(false);

            return found.TryGetValue(tenantId, out var metadata) && !string.IsNullOrWhiteSpace(metadata.TimeZoneId)
                ? TimeZoneInfo.FindSystemTimeZoneById(metadata.TimeZoneId)
                : null;
        }
    }
}
