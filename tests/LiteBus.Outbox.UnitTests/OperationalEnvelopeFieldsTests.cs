using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Covers the operational fields an operator dashboard reaches for first: when a message started failing, when it
///     gave up, which instance was handling it, and what kind of failure it was.
/// </summary>
public sealed class OperationalEnvelopeFieldsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Leasing_records_the_owner_so_it_survives_the_lease_being_released()
    {
        var leased = CreatePending().AsLeased("publisher-1", Now.AddMinutes(1));

        leased.LeaseOwner.Should().Be("publisher-1");
        leased.LastLeaseOwner.Should().Be("publisher-1");
    }

    [Fact]
    public void A_failure_records_its_type_and_the_moment_it_started()
    {
        var leased = CreatePending().AsLeased("publisher-1", Now.AddMinutes(1));
        var failed = leased.AsFailed(MessageFailure.From(new TimeoutException("broker down"), Now));

        failed.ErrorType.Should().Be(typeof(TimeoutException).FullName);
        failed.FirstFailedAt.Should().Be(Now);
        failed.DeadLetteredAt.Should().BeNull();
        failed.LastLeaseOwner.Should().Be("publisher-1");
    }

    [Fact]
    public void A_later_failure_does_not_move_the_first_failure_timestamp()
    {
        var first = CreatePending()
            .AsLeased("publisher-1", Now.AddMinutes(1))
            .AsFailed(MessageFailure.From(new TimeoutException("first"), Now));

        var second = first
            .AsLeased("publisher-2", Now.AddHours(1))
            .AsFailed(MessageFailure.From(new TimeoutException("second"), Now.AddHours(1)));

        // It answers how long the message struggled, which only works if it keeps naming the first failure.
        second.FirstFailedAt.Should().Be(Now);
        second.LastLeaseOwner.Should().Be("publisher-2");
    }

    [Fact]
    public void Dead_lettering_records_when_the_message_gave_up()
    {
        var leased = CreatePending().AsLeased("publisher-1", Now.AddMinutes(1));
        var deadLettered = leased.AsDeadLettered(MessageFailure.From(new InvalidOperationException("poison"), Now));

        deadLettered.DeadLetteredAt.Should().Be(Now);
        deadLettered.FirstFailedAt.Should().Be(Now);
        deadLettered.ErrorType.Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public void Requeueing_clears_the_failure_record_and_keeps_the_attempt_history()
    {
        var deadLettered = CreatePending()
            .AsLeased("publisher-1", Now.AddMinutes(1))
            .AsDeadLettered(MessageFailure.From(new InvalidOperationException("poison"), Now));

        var requeued = deadLettered.AsRequeued();

        requeued.DeadLetteredAt.Should().BeNull();
        requeued.FirstFailedAt.Should().BeNull();
        requeued.ErrorType.Should().BeNull();
        requeued.LastError.Should().BeNull();
        requeued.LastLeaseOwner.Should().Be("publisher-1");
    }

    [Fact]
    public void A_failure_without_an_exception_records_no_type()
    {
        var leased = CreatePending().AsLeased("publisher-1", Now.AddMinutes(1));
        var failed = leased.AsFailed(MessageFailure.FromError("the lease was lost during processing", Now));

        failed.ErrorType.Should().BeNull();
        failed.LastError.Should().Be("the lease was lost during processing");
        failed.FirstFailedAt.Should().Be(Now);
    }

    [Fact]
    public void Publishing_clears_the_error_type_along_with_the_error()
    {
        var published = CreatePending()
            .AsLeased("publisher-1", Now.AddMinutes(1))
            .AsFailed(MessageFailure.From(new TimeoutException("transient"), Now))
            .AsLeased("publisher-1", Now.AddHours(1))
            .AsPublished(Now.AddHours(1));

        published.LastError.Should().BeNull();
        published.FirstFailedAt.Should().Be(Now, "the struggle still happened and is worth reporting");
    }

    private static OutboxEnvelope CreatePending()
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.operational",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = Now,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }
}
