using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox manager replay semantics report honest requeue counts.
/// </summary>
public sealed class InboxManagerRequeueTests
{
    /// <summary>
    ///     Verifies requeue reports requested versus actually requeued rows.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_should_return_requested_and_requeued_counts()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);

        var deadLetterId = Guid.NewGuid();
        var pendingId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = deadLetterId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.DeadLettered
        });

        await store.AddAsync(new InboxEnvelope
        {
            Id = pendingId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        });

        var result = await manager.RequeueAsync([deadLetterId, pendingId, Guid.NewGuid()]);

        result.Requested.Should().Be(3);
        result.Requeued.Should().Be(1);
        store.Get(deadLetterId).Status.Should().Be(InboxStatus.Pending);
        store.Get(pendingId).Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Creates an <see cref="InboxManager" /> backed by the supplied store roles.
    /// </summary>
    /// <param name="store">The in-memory store used for operations and retention.</param>
    /// <returns>The configured inbox manager.</returns>
    private static InboxManager CreateManager(InMemoryInboxStore store)
    {
        var cleanupOptions = new InboxCleanupHostOptions();

        return new InboxManager(
            store,
            store,
            new InboxRetentionCoordinator(cleanupOptions),
            cleanupOptions,
            TimeProvider.System);
    }
}
