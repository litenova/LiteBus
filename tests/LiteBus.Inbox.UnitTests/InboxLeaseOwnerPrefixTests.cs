using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies the persisted lease owner carries the configured prefix plus a session suffix.
/// </summary>
public sealed class InboxLeaseOwnerPrefixTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the persisted owner starts with the configured value and a colon separator.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPersistOwnerWithConfiguredPrefixAndSessionSuffix()
    {
        var store = new InMemoryInboxStore();

        var processor = new PipelinedInboxProcessor(
            store,
            store,
            new SuccessInboxDispatcher(),
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "prefix-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1
            },
            TimeProvider.System,
            []);

        var commandId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(1);
        store.Get(commandId).Status.Should().Be(InboxStatus.Completed);
        store.Get(commandId).LastLeaseOwner.Should().StartWith("prefix-worker:");
    }

    /// <summary>
    ///     Test dispatcher that succeeds without side effects.
    /// </summary>
    private sealed class SuccessInboxDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
