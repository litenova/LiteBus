using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies the persisted lease owner carries the configured prefix plus a session suffix.
/// </summary>
public sealed class OutboxLeaseOwnerPrefixTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies the persisted owner starts with the configured value and a colon separator.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPersistOwnerWithConfiguredPrefixAndSessionSuffix()
    {
        var store = new InMemoryOutboxStore();

        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            new SuccessOutboxDispatcher(),
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "prefix-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                DispatcherConcurrency = 1
            },
            TimeProvider.System,
            []);

        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.SucceededCount.Should().Be(1);
        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
        store.Get(messageId).LastLeaseOwner.Should().StartWith("prefix-worker:");
    }

    /// <summary>
    ///     Test dispatcher that succeeds without side effects.
    /// </summary>
    private sealed class SuccessOutboxDispatcher : IOutboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
