using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies pipelined outbox processor terminal state persistence behavior.
/// </summary>
public sealed class OutboxProcessorBulkTerminalStateTests
{
    /// <summary>
    ///     Confirms each dead-letter transition is persisted once per message in the pipelined processor.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldPersistEachDeadLetter()
    {
        var inner = new InMemoryOutboxStore();
        var processingStore = new CountingOutboxProcessingStore(inner);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero));
        var dispatcher = new AlwaysFailingOutboxDispatcher();

        var processor = new PipelinedOutboxProcessor(
            processingStore,
            processingStore,
            dispatcher,
            new OutboxProcessorOptions
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
            await inner.AddAsync(new OutboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.events.submitted",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = clock.GetUtcNow(),
                Status = OutboxStatus.Pending,
                AttemptCount = 0
            });
        }

        var result = await processor.ProcessPendingAsync();

        result.DeadLetteredCount.Should().Be(3);
        processingStore.PersistCallCount.Should().Be(3);
        processingStore.LastPersistedDeadLetterCount.Should().Be(1);
    }

    /// <summary>
    ///     Counting store wrapper that tracks terminal persist calls.
    /// </summary>
    private sealed class CountingOutboxProcessingStore : IOutboxProcessingStore
    {
        /// <summary>
        ///     The inner store that owns envelope state.
        /// </summary>
        private readonly InMemoryOutboxStore _inner;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CountingOutboxProcessingStore" /> class.
        /// </summary>
        /// <param name="inner">The inner store that owns envelope state.</param>
        public CountingOutboxProcessingStore(InMemoryOutboxStore inner)
        {
            _inner = inner;
        }

        /// <summary>
        ///     Gets the number of <see cref="PersistAsync" /> calls.
        /// </summary>
        public int PersistCallCount { get; private set; }

        /// <summary>
        ///     Gets the dead-letter count in the last persist call.
        /// </summary>
        public int LastPersistedDeadLetterCount { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
            OutboxLeaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return _inner.LeasePendingAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            return _inner.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PersistResult> PersistAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            PersistCallCount++;
            LastPersistedDeadLetterCount = envelopes.Count(envelope => envelope.Status == OutboxStatus.DeadLettered);
            return _inner.PersistAsync(envelopes, cancellationToken);
        }
    }

    /// <summary>
    ///     Dispatcher that always fails publication.
    /// </summary>
    private sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
    {
        /// <inheritdoc />
        public Task DispatchAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated publish failure.");
        }
    }
}