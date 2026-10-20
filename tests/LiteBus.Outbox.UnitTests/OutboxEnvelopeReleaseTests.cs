using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies <see cref="OutboxEnvelope.AsReleased" /> returns leased rows to the queue without charging an attempt.
/// </summary>
public sealed class OutboxEnvelopeReleaseTests
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

        released.Status.Should().Be(OutboxStatus.Pending);
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
        var envelope = CreateLeased(attemptCount: 3, lastError: "dispatcher fault");

        var released = envelope.AsReleased();

        released.Status.Should().Be(OutboxStatus.Failed);
        released.AttemptCount.Should().Be(2);
        released.LastError.Should().Be("dispatcher fault");
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

        released.Status.Should().Be(OutboxStatus.Pending);
        released.AttemptCount.Should().Be(0);
    }

    /// <summary>
    ///     Confirms release is rejected for envelopes that do not hold an active lease.
    /// </summary>
    [Theory]
    [InlineData(OutboxStatus.Pending)]
    [InlineData(OutboxStatus.Published)]
    [InlineData(OutboxStatus.Failed)]
    [InlineData(OutboxStatus.DeadLettered)]
    public void AsReleased_WhenNotPublishing_ShouldThrow(OutboxStatus status)
    {
        var envelope = CreateLeased(attemptCount: 1) with { Status = status };

        var act = () => envelope.AsReleased();

        act.Should().Throw<InvalidOperationException>();
    }

    private static OutboxEnvelope CreateLeased(int attemptCount, DateTimeOffset? visibleAfter = null, string? lastError = null)
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            VisibleAfter = visibleAfter,
            Status = OutboxStatus.Publishing,
            AttemptCount = attemptCount,
            LeaseOwner = "worker-1",
            LeaseGeneration = 4,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = lastError
        };
    }
}
