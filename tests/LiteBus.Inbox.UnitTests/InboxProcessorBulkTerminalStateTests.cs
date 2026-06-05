using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies the inbox processor batches terminal state updates into single store calls.
/// </summary>
public sealed class InboxProcessorBulkTerminalStateTests
{
    /// <summary>
    ///     Confirms each dead-letter transition is persisted per message and the pass <c>finally</c> block persists the
    ///     accumulated batch.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_max_attempts_exceeded_should_persist_per_message_and_in_finally()
    {
        var inner = new InMemoryInboxStore();
        var processingStore = new CountingInboxProcessingStore(inner);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = new AlwaysFailingInboxDispatcher();

        var processor = new InboxProcessor(
            processingStore,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 5,
                LeaseOwner = "bulk-test",
                LeaseDuration = TimeSpan.FromMinutes(1),
                Retry = new RetryOptions { MaxAttempts = 1, UseJitter = false }
            },
            clock);

        for (var index = 0; index < 3; index++)
        {
            await inner.EnqueueAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = clock.GetUtcNow(),
                Status = InboxStatus.Pending,
                AttemptCount = 0
            });
        }

        var result = await processor.ProcessPendingAsync();

        result.DeadLetteredCount.Should().Be(3);
        processingStore.PersistCallCount.Should().Be(4);
        processingStore.LastPersistedDeadLetterCount.Should().Be(3);
    }

    /// <summary>
    ///     Dispatcher that always fails so the processor records failures or dead letters.
    /// </summary>
    private sealed class AlwaysFailingInboxDispatcher : IInboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Dispatch failed.");
        }
    }

    /// <summary>
    ///     Processing store that counts persist calls and dead-letter batch sizes.
    /// </summary>
    private sealed class CountingInboxProcessingStore : IInboxProcessingStore
    {
        private readonly InMemoryInboxStore _inner;

        public CountingInboxProcessingStore(InMemoryInboxStore inner)
        {
            _inner = inner;
        }

        public int PersistCallCount { get; private set; }

        public int LastPersistedDeadLetterCount { get; private set; }

        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(envelope, cancellationToken);

        public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            _inner.AddBatchAsync(envelopes, cancellationToken);

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.LeasePendingAsync(request, cancellationToken);

        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            _inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        public Task PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            PersistCallCount++;
            LastPersistedDeadLetterCount = envelopes.Count(envelope => envelope.Status == InboxStatus.DeadLettered);
            return _inner.PersistAsync(envelopes, cancellationToken);
        }
    }
}
