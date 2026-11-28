using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     Verifies that the checkpoint policy factory builds record-count, time-interval, and combined policies.
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
    /// <returns>The candidate.</returns>
    private static AuditCheckpointCandidate Candidate(
        long sequence,
        long count,
        long? lastCheckpointSequence = null,
        DateTimeOffset? lastCheckpointAt = null,
        DateTimeOffset? now = null)
    {
        return new AuditCheckpointCandidate(
            Segment,
            sequence,
            count,
            lastCheckpointSequence,
            lastCheckpointAt,
            now ?? new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero));
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
    public void EveryRecords_is_not_time_based()
    {
        CheckpointPolicies.EveryRecords(100).IsTimeBased.Should().BeFalse();
    }

    [Fact]
    public void Every_rejects_a_non_positive_interval()
    {
        var act = () => CheckpointPolicies.Every(TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_rejects_a_negative_interval()
    {
        var act = () => CheckpointPolicies.Every(TimeSpan.FromHours(-1));

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
    public void Every_is_due_for_records_without_a_checkpoint()
    {
        var policy = CheckpointPolicies.Every(TimeSpan.FromHours(1));

        policy.IsDue(Candidate(sequence: 1, count: 1)).Should().BeTrue();
    }

    [Fact]
    public void Every_is_never_due_without_records()
    {
        var policy = CheckpointPolicies.Every(TimeSpan.FromHours(1));

        policy.IsDue(Candidate(sequence: 0, count: 0)).Should().BeFalse();
    }

    [Fact]
    public void Every_is_time_based()
    {
        CheckpointPolicies.Every(TimeSpan.FromHours(1)).IsTimeBased.Should().BeTrue();
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
    public void Any_without_policies_is_never_due()
    {
        CheckpointPolicies.Any()
            .IsDue(Candidate(sequence: 100, count: 100))
            .Should().BeFalse();
    }

    [Fact]
    public void Any_is_time_based_when_any_policy_is()
    {
        CheckpointPolicies.Any(CheckpointPolicies.EveryRecords(100)).IsTimeBased.Should().BeFalse();
        CheckpointPolicies
            .Any(CheckpointPolicies.EveryRecords(100), CheckpointPolicies.Every(TimeSpan.FromHours(1)))
            .IsTimeBased.Should().BeTrue();
    }

    [Fact]
    public void Any_rejects_a_null_entry()
    {
        var act = () => CheckpointPolicies.Any(CheckpointPolicies.EveryRecords(100), null!);

        act.Should().Throw<ArgumentException>();
    }
}
