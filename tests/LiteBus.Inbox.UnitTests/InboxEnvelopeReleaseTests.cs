using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="InboxEnvelope.AsReleased" /> returns leased rows to the queue without charging an attempt.
/// </summary>
public sealed class InboxEnvelopeReleaseTests
{
    /// <summary>
    ///     Confirms a clean leased envelope returns to pending with its lease cleared and one attempt refunded.
    /// </summary>
    [Fact]
    public void AsReleased_WhenNoError_ShouldReturnPendingWithLeaseCleared()
    {
        var visibleAfter = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var envelope = CreateLeased(attemptCount: 2, visibleAfter: visibleAfter);

        var released = envelope.AsReleased();

        released.Status.Should().Be(InboxStatus.Pending);
        released.AttemptCount.Should().Be(1);
        released.LeaseOwner.Should().BeNull();
        released.LeaseExpiresAt.Should().BeNull();
        released.VisibleAfter.Should().Be(visibleAfter);
        released.LeaseGeneration.Should().Be(envelope.LeaseGeneration);
    }

    /// <summary>
    ///     Confirms a leased envelope that already carries an error returns to failed instead of pending.
    /// </summary>
    [Fact]
    public void AsReleased_WhenLastErrorSet_ShouldReturnFailed()
    {
        var envelope = CreateLeased(attemptCount: 3, lastError: "handler fault");

        var released = envelope.AsReleased();

        released.Status.Should().Be(InboxStatus.Failed);
        released.AttemptCount.Should().Be(2);
        released.LastError.Should().Be("handler fault");
        released.LeaseOwner.Should().BeNull();
        released.LeaseExpiresAt.Should().BeNull();
    }

    /// <summary>
    ///     Confirms the attempt refund never drops the count below zero.
    /// </summary>
    [Fact]
    public void AsReleased_WhenAttemptCountIsZero_ShouldFloorAtZero()
    {
        var envelope = CreateLeased(attemptCount: 0);

        var released = envelope.AsReleased();

        released.Status.Should().Be(InboxStatus.Pending);
        released.AttemptCount.Should().Be(0);
    }

    /// <summary>
    ///     Confirms release is rejected for envelopes that do not hold an active lease.
    /// </summary>
    [Theory]
    [InlineData(InboxStatus.Pending)]
    [InlineData(InboxStatus.Completed)]
    [InlineData(InboxStatus.Failed)]
    [InlineData(InboxStatus.DeadLettered)]
    public void AsReleased_WhenNotProcessing_ShouldThrow(InboxStatus status)
    {
        var envelope = CreateLeased(attemptCount: 1) with { Status = status };

        var act = () => envelope.AsReleased();

        act.Should().Throw<InvalidOperationException>();
    }

    private static InboxEnvelope CreateLeased(int attemptCount, DateTimeOffset? visibleAfter = null, string? lastError = null)
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            VisibleAfter = visibleAfter,
            Status = InboxStatus.Processing,
            AttemptCount = attemptCount,
            LeaseOwner = "worker-1",
            LeaseGeneration = 4,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = lastError
        };
    }
}
