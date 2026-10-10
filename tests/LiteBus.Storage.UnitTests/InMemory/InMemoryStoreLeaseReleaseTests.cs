using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.UnitTests.InMemory;

/// <summary>
///     Verifies lease release on the in-memory inbox and outbox stores.
/// </summary>
public sealed class InMemoryStoreLeaseReleaseTests
{
    /// <summary>
    ///     Confirms an outbox release with matching owner and generation returns the row to the queue.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenOwnerAndGenerationMatch_ShouldReturnRowToQueue()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingOutbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = store.Get(messageId);
        row.Status.Should().Be(OutboxStatus.Pending);
        row.AttemptCount.Should().Be(0);
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        var leasedAgain = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leasedAgain.Should().ContainSingle(envelope => envelope.Id == messageId);
        leasedAgain[0].LeaseOwner.Should().Be("worker-2");
    }

    /// <summary>
    ///     Confirms an outbox release with a wrong owner or stale generation fails and leaves the row leased.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenOwnerOrGenerationMismatch_ShouldReturnFalseAndChangeNothing()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingOutbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();

        var wrongOwner = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-2", leased[0].LeaseGeneration)).ConfigureAwait(false);
        wrongOwner.Should().BeFalse();

        var staleGeneration = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration + 1)).ConfigureAwait(false);
        staleGeneration.Should().BeFalse();

        var row = store.Get(messageId);
        row.Status.Should().Be(OutboxStatus.Publishing);
        row.LeaseOwner.Should().Be("worker-1");
        row.LeaseGeneration.Should().Be(leased[0].LeaseGeneration);
        row.AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);
        released.Should().BeTrue();
    }

    /// <summary>
    ///     Confirms an outbox release of a row that carries an error yields failed instead of pending.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenRowCarriesError_ShouldYieldFailed()
    {
        var store = new InMemoryOutboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 2,
            Status = OutboxStatus.Publishing,
            LeaseOwner = "worker-1",
            LeaseGeneration = 2,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = "dispatcher fault"
        }).ConfigureAwait(false);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", 2)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = store.Get(messageId);
        row.Status.Should().Be(OutboxStatus.Failed);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("dispatcher fault");
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();
    }

    /// <summary>
    ///     Confirms an inbox release with matching owner and generation returns the row to the queue.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenOwnerAndGenerationMatch_ShouldReturnRowToQueue()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingInbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = store.Get(messageId);
        row.Status.Should().Be(InboxStatus.Pending);
        row.AttemptCount.Should().Be(0);
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        var leasedAgain = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leasedAgain.Should().ContainSingle(envelope => envelope.Id == messageId);
        leasedAgain[0].LeaseOwner.Should().Be("worker-2");
    }

    /// <summary>
    ///     Confirms an inbox release with a wrong owner or stale generation fails and leaves the row leased.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenOwnerOrGenerationMismatch_ShouldReturnFalseAndChangeNothing()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingInbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();

        var wrongOwner = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-2", leased[0].LeaseGeneration)).ConfigureAwait(false);
        wrongOwner.Should().BeFalse();

        var staleGeneration = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration + 1)).ConfigureAwait(false);
        staleGeneration.Should().BeFalse();

        var row = store.Get(messageId);
        row.Status.Should().Be(InboxStatus.Processing);
        row.LeaseOwner.Should().Be("worker-1");
        row.LeaseGeneration.Should().Be(leased[0].LeaseGeneration);
        row.AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);
        released.Should().BeTrue();
    }

    /// <summary>
    ///     Confirms an inbox release of a row that carries an error yields failed instead of pending.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenRowCarriesError_ShouldYieldFailed()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 2,
            Status = InboxStatus.Processing,
            LeaseOwner = "worker-1",
            LeaseGeneration = 2,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = "handler fault"
        }).ConfigureAwait(false);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", 2)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = store.Get(messageId);
        row.Status.Should().Be(InboxStatus.Failed);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("handler fault");
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();
    }

    private static OutboxEnvelope CreatePendingOutbox(Guid messageId, DateTimeOffset now)
    {
        return new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        };
    }

    private static InboxEnvelope CreatePendingInbox(Guid messageId, DateTimeOffset now)
    {
        return new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        };
    }
}
