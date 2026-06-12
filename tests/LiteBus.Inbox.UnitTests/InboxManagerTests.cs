using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="IInboxManager" /> browse, replay, purge, and retention operations.
/// </summary>
public sealed class InboxManagerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies selective dead-letter replay by message identifier.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_WithMessageIds_ShouldReturnRowsToPending()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);
        var messageId = Guid.NewGuid();

        await SeedDeadLetterAsync(store, messageId);

        var requeued = await manager.RequeueAsync([messageId]);

        requeued.Requested.Should().Be(1);
        requeued.Requeued.Should().Be(1);

        var page = await manager.QueryAsync(
            new InboxMessageFilter { Statuses = [InboxStatus.Pending] },
            new InboxMessagePageRequest { PageSize = 10 });

        page.Items.Should().ContainSingle(item => item.Id == messageId);
    }

    /// <summary>
    ///     Verifies purge requires confirmation when the filter is unrestricted.
    /// </summary>
    [Fact]
    public async Task PurgeAsync_WithoutConfirm_ShouldThrowForUnrestrictedFilter()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);

        var act = async () => await manager.PurgeAsync(new InboxMessageFilter(), false);

        await act.Should().ThrowAsync<InboxManagementException>();
    }

    /// <summary>
    ///     Verifies <see cref="IInboxManager.GetMessageAsync" /> returns a stored envelope.
    /// </summary>
    [Fact]
    public async Task GetMessageAsync_ShouldReturnStoredEnvelope()
    {
        var store = new InMemoryInboxStore();
        var manager = CreateManager(store);
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        });

        var message = await manager.GetMessageAsync(messageId);

        message.Should().NotBeNull();
        message!.Id.Should().Be(messageId);
    }

    /// <summary>
    ///     Creates an <see cref="InboxManager" /> backed by the supplied store roles.
    /// </summary>
    /// <param name="store">The in-memory store implementing operations and retention roles.</param>
    /// <returns>The manager under test.</returns>
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

    /// <summary>
    ///     Seeds one dead-lettered inbox message.
    /// </summary>
    /// <param name="store">The store receiving the envelope.</param>
    /// <param name="messageId">The message identifier.</param>
    private static async Task SeedDeadLetterAsync(InMemoryInboxStore store, Guid messageId)
    {
        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        });

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.PersistAsync([leased[0].AsDeadLettered("exhausted")]);
    }
}