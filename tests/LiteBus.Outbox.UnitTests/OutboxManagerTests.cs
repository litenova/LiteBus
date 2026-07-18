using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies <see cref="IOutboxManager" /> browse, replay, and purge operations.
/// </summary>
public sealed class OutboxManagerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies selective dead-letter replay by message identifier.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_WithMessageIds_ShouldReturnRowsToPending()
    {
        var store = new InMemoryOutboxStore();
        var manager = CreateManager(store);
        var messageId = Guid.NewGuid();

        await SeedDeadLetterAsync(store, messageId).ConfigureAwait(true);

        var requeued = await manager.RequeueAsync([messageId]).ConfigureAwait(true);

        requeued.Requested.Should().Be(1);
        requeued.Requeued.Should().Be(1);

        var page = await manager.QueryAsync(
            new OutboxMessageFilter { Statuses = [OutboxStatus.Pending] },
            new OutboxMessagePageRequest { PageSize = 10 });

        page.Items.Should().ContainSingle(item => item.Id == messageId);
    }

    /// <summary>
    ///     Verifies purge requires confirmation when the filter is unrestricted.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithoutConfirm_ShouldThrowForUnrestrictedFilter()
    {
        var store = new InMemoryOutboxStore();
        var manager = CreateManager(store);

        var act = async () => await manager.PurgeAsync(new OutboxMessageFilter(), false).ConfigureAwait(true);

        await act.Should().ThrowAsync<OutboxManagementException>();
    }

    /// <summary>
    ///     Creates an <see cref="OutboxManager" /> backed by the supplied store roles.
    /// </summary>
    /// <param name="store">The in-memory store implementing operations and retention roles.</param>
    /// <returns>The manager under test.</returns>
    private static OutboxManager CreateManager(InMemoryOutboxStore store)
    {
        var cleanupOptions = new OutboxCleanupHostOptions();

        return new OutboxManager(
            store,
            store,
            new OutboxRetentionCoordinator(cleanupOptions),
            cleanupOptions,
            TimeProvider.System);
    }

    /// <summary>
    ///     Seeds one dead-lettered outbox message.
    /// </summary>
    /// <param name="store">The store receiving the envelope.</param>
    /// <param name="messageId">The message identifier.</param>
    private static async Task SeedDeadLetterAsync(InMemoryOutboxStore store, Guid messageId)
    {
        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        }).ConfigureAwait(false);


        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "publisher",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        await store.PersistAsync([leased[0].AsDeadLettered("exhausted")]).ConfigureAwait(false);
    }
}
