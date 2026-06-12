using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies pipelined inbox processor terminal state persistence behavior.
/// </summary>
public sealed class InboxProcessorBulkTerminalStateTests
{
    /// <summary>
    ///     Confirms each dead-letter transition is persisted once per message in the pipelined processor.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_max_attempts_exceeded_should_persist_each_dead_letter()
    {
        var inner = new InMemoryInboxStore();
        var processingStore = new CountingInboxProcessingStore(inner);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = new AlwaysFailingInboxDispatcher();

        var processor = new PipelinedInboxProcessor(
            processingStore,
            processingStore,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 5,
                LeaseOwner = "bulk-test",
                LeaseDuration = TimeSpan.FromMinutes(1),
                Retry = new RetryOptions { MaxAttempts = 1, UseJitter = false }
            },
            clock,
            Array.Empty<IProcessorEnvelopeHook>());

        for (var index = 0; index < 3; index++)
        {
            await inner.AddAsync(new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.commands.ship",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = clock.GetUtcNow(),
                Status = InboxStatus.Pending,
                AttemptCount = 0
            }).ConfigureAwait(false);
        }

        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.DeadLetteredCount.Should().Be(3);
        processingStore.PersistCallCount.Should().Be(3);
        processingStore.LastPersistedDeadLetterCount.Should().Be(1);
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

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return _inner.LeasePendingAsync(request, cancellationToken);
        }

        public Task<bool> RenewLeaseAsync(
            LeaseRenewalRequest request,
            CancellationToken cancellationToken = default)
        {
            return _inner.RenewLeaseAsync(request, cancellationToken);
        }

        public async Task<PersistResult> PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            PersistCallCount++;
            LastPersistedDeadLetterCount = envelopes.Count(envelope => envelope.Status == InboxStatus.DeadLettered);
            return await _inner.PersistAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }
    }
}