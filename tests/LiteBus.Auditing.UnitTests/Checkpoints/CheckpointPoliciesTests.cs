using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Scheduling;
using LiteBus.Runtime.Scheduling;

namespace LiteBus.Auditing.UnitTests.Checkpoints;

/// <summary>
///     Verifies that the checkpoint policy factory builds silent, record-count, time-interval, combined, and calendar
///     policies with their labels.
/// </summary>
public sealed class CheckpointPoliciesTests
{
    /// <summary>
    ///     The segment every candidate in these tests belongs to.
    /// </summary>
    private const string Segment = "orders/2026";

    /// <summary>
    ///     Builds the candidate a policy decides on.
    /// </summary>
    /// <param name="sequence">The sequence the segment has reached.</param>
    /// <param name="count">The number of documents in the segment.</param>
    /// <param name="lastCheckpointSequence">The sequence of the last checkpoint, or <see langword="null" /> when none was cut.</param>
    /// <param name="lastCheckpointAt">When the last checkpoint was cut, or <see langword="null" /> when none was cut.</param>
    /// <param name="now">The current time.</param>
    /// <param name="segmentInfo">The segment identity, or <see langword="null" /> when the caller supplies none.</param>
    /// <returns>The candidate.</returns>
    private static AuditCheckpointCandidate Candidate(
        long sequence,
        long count,
        long? lastCheckpointSequence = null,
        DateTimeOffset? lastCheckpointAt = null,
        DateTimeOffset? now = null,
        AuditSegmentInfo? segmentInfo = null)
    {
        return new AuditCheckpointCandidate(
            Segment,
            sequence,
            count,
            lastCheckpointSequence,
            lastCheckpointAt,
            now ?? new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            segmentInfo);
    }

    /// <summary>
    ///     Builds segment identity with a tenant and an opening instant.
    /// </summary>
    /// <param name="tenantId">The tenant, or <see langword="null" /> for an unscoped segment.</param>
    /// <param name="openedAt">When the segment was opened.</param>
    /// <returns>The segment identity.</returns>
    private static AuditSegmentInfo SegmentInfo(string? tenantId, DateTimeOffset openedAt)
    {
        return new AuditSegmentInfo(Segment, tenantId, new Dictionary<string, string>(), openedAt);
    }

    [Fact]
    public void Never_is_never_due()
    {
        var policy = CheckpointPolicies.Never();

        policy.IsDue(Candidate(sequence: 100, count: 100)).Should().BeFalse();
        policy.IsTimeBased.Should().BeFalse();
    }

    [Fact]
    public async Task Never_is_never_due_async()
    {
        var policy = CheckpointPolicies.Never();

        (await policy.IsDueAsync(Candidate(sequence: 100, count: 100), CancellationToken.None).ConfigureAwait(false))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EveryRecords_rejects_a_non_positive_interval(int recordCount)
    {
        var act = () => CheckpointPolicies.EveryRecords(recordCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EveryRecords_is_due_once_the_interval_is_reached()
    {
        var policy = CheckpointPolicies.EveryRecords(100);

        policy.IsDue(Candidate(sequence: 99, count: 99)).Should().BeFalse();
        policy.IsDue(Candidate(sequence: 100, count: 100)).Should().BeTrue();
        policy.IsDue(Candidate(sequence: 199, count: 199, lastCheckpointSequence: 100)).Should().BeFalse();
        policy.IsDue(Candidate(sequence: 200, count: 200, lastCheckpointSequence: 100)).Should().BeTrue();
    }

    [Fact]
    public void EveryRecords_carries_its_label()
    {
        var policy = (ILabeledCheckpointPolicy) CheckpointPolicies.EveryRecords(100, label: "nightly");

        policy.Label.Should().Be("nightly");
    }

    [Fact]
    public void EveryRecords_rejects_a_blank_label()
    {
        var act = () => CheckpointPolicies.EveryRecords(100, label: "  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_rejects_a_non_positive_interval()
    {
        var act = () => CheckpointPolicies.Every(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_is_due_once_the_interval_has_passed()
    {
        var policy = CheckpointPolicies.Every(TimeSpan.FromHours(1));
        var now = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        policy.IsDue(Candidate(sequence: 10, count: 10, lastCheckpointAt: now.AddMinutes(-30), now: now))
            .Should().BeFalse();
        policy.IsDue(Candidate(sequence: 10, count: 10, lastCheckpointAt: now.AddHours(-2), now: now))
            .Should().BeTrue();
    }

    [Fact]
    public void Every_is_never_due_without_records()
    {
        var policy = CheckpointPolicies.Every(TimeSpan.FromHours(1));

        policy.IsDue(Candidate(sequence: 0, count: 0)).Should().BeFalse();
    }

    [Fact]
    public void Any_is_due_when_any_policy_is_due()
    {
        var due = CheckpointPolicies.EveryRecords(100);
        var notDue = CheckpointPolicies.Every(TimeSpan.FromDays(30));
        var now = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        CheckpointPolicies.Any(due, notDue)
            .IsDue(Candidate(sequence: 100, count: 100, lastCheckpointAt: now, now: now))
            .Should().BeTrue();
        CheckpointPolicies.Any(notDue)
            .IsDue(Candidate(sequence: 100, count: 100, lastCheckpointAt: now, now: now))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Any_evaluates_async_policies()
    {
        var now = new DateTimeOffset(2026, 5, 5, 1, 0, 0, TimeSpan.Zero);
        var openedAt = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var tenant = CheckpointPolicies.DailyAt(
            new TimeOnly(0, 0),
            (_, _) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));
        var policy = CheckpointPolicies.Any(CheckpointPolicies.Never(), tenant);
        var candidate = Candidate(
            sequence: 10,
            count: 10,
            lastCheckpointAt: openedAt,
            now: now,
            segmentInfo: SegmentInfo("tenant-a", openedAt));

        (await policy.IsDueAsync(candidate, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
        policy.IsDue(candidate).Should().BeFalse();
    }

    [Fact]
    public void Any_rejects_a_null_entry()
    {
        var act = () => CheckpointPolicies.Any(CheckpointPolicies.EveryRecords(100), null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OnSchedule_is_due_after_the_next_occurrence()
    {
        var schedule = Schedules.Every(TimeSpan.FromHours(1));
        var policy = CheckpointPolicies.OnSchedule(schedule);
        var last = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        policy.IsTimeBased.Should().BeTrue();
        policy.IsDue(Candidate(sequence: 5, count: 5, lastCheckpointAt: last, now: last.AddMinutes(30)))
            .Should().BeFalse();
        policy.IsDue(Candidate(sequence: 5, count: 5, lastCheckpointAt: last, now: last.AddHours(2)))
            .Should().BeTrue();
    }

    [Fact]
    public void OnSchedule_is_never_due_without_records()
    {
        var policy = CheckpointPolicies.OnSchedule(Schedules.Every(TimeSpan.FromHours(1)));

        policy.IsDue(Candidate(sequence: 0, count: 0)).Should().BeFalse();
    }

    [Fact]
    public void OnSchedule_is_due_immediately_without_an_anchor()
    {
        var policy = CheckpointPolicies.OnSchedule(Schedules.Every(TimeSpan.FromHours(1)));

        policy.IsDue(Candidate(sequence: 1, count: 1)).Should().BeTrue();
    }

    [Fact]
    public void DailyAt_cuts_at_the_local_midnight_after_the_last_checkpoint()
    {
        var policy = CheckpointPolicies.DailyAt(new TimeOnly(0, 0), TimeZoneInfo.Utc);
        var last = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        ((ILabeledCheckpointPolicy) policy).Label.Should().Be("daily");
        policy.IsDue(Candidate(sequence: 5, count: 5, lastCheckpointAt: last, now: last.AddHours(6)))
            .Should().BeFalse();
        policy.IsDue(
                Candidate(
                    sequence: 5,
                    count: 5,
                    lastCheckpointAt: last,
                    now: new DateTimeOffset(2026, 5, 5, 0, 0, 1, TimeSpan.Zero)))
            .Should().BeTrue();
    }

    [Fact]
    public void WeeklyOn_cuts_on_the_configured_weekday()
    {
        var policy = CheckpointPolicies.WeeklyOn(DayOfWeek.Monday, new TimeOnly(9, 0), TimeZoneInfo.Utc);
        var last = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

        ((ILabeledCheckpointPolicy) policy).Label.Should().Be("weekly");
        policy.IsDue(Candidate(sequence: 5, count: 5, lastCheckpointAt: last, now: last.AddDays(1)))
            .Should().BeFalse();
        policy.IsDue(
                Candidate(
                    sequence: 5,
                    count: 5,
                    lastCheckpointAt: last,
                    now: new DateTimeOffset(2026, 5, 11, 9, 0, 1, TimeSpan.Zero)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DailyAt_with_a_tenant_zone_reads_midnight_in_that_zone()
    {
        var policy = CheckpointPolicies.DailyAt(
            new TimeOnly(0, 0),
            (_, _) => ValueTask.FromResult<TimeZoneInfo?>(TimeZoneInfo.Utc));
        var openedAt = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var beforeMidnight = Candidate(
            sequence: 5,
            count: 5,
            lastCheckpointAt: openedAt,
            now: new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            segmentInfo: SegmentInfo("tenant-a", openedAt));
        var afterMidnight = beforeMidnight with
        {
            Now = new DateTimeOffset(2026, 5, 5, 0, 0, 1, TimeSpan.Zero),
        };

        policy.IsDue(beforeMidnight).Should().BeFalse();
        (await policy.IsDueAsync(beforeMidnight, CancellationToken.None).ConfigureAwait(false)).Should().BeFalse();
        (await policy.IsDueAsync(afterMidnight, CancellationToken.None).ConfigureAwait(false)).Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_schedule_without_a_tenant_cuts_only_the_first_checkpoint()
    {
        var policy = CheckpointPolicies.OnTenantSchedule(new NeverTenantSchedule());
        var openedAt = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);

        (await policy
            .IsDueAsync(Candidate(sequence: 5, count: 5, segmentInfo: SegmentInfo(null, openedAt)), CancellationToken.None)
            .ConfigureAwait(false)).Should().BeTrue();
        (await policy
            .IsDueAsync(
                Candidate(sequence: 5, count: 5, lastCheckpointAt: openedAt, segmentInfo: SegmentInfo(null, openedAt)),
                CancellationToken.None)
            .ConfigureAwait(false)).Should().BeFalse();
    }

    /// <summary>
    ///     A tenant schedule that never fires, so the fallback for missing tenants is observable.
    /// </summary>
    private sealed class NeverTenantSchedule : ITenantSchedule
    {
        /// <inheritdoc />
        public ValueTask<DateTimeOffset?> GetNextOccurrenceAsync(
            string tenantId,
            DateTimeOffset after,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<DateTimeOffset?>(null);
        }
    }
}
