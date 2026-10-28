using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies a single enqueue persists its whole per-handler fan-out in one batch call.
/// </summary>
public sealed class R3F3AtomicFanOutTests
{
    /// <summary>
    ///     Verifies the immediate writer calls AddBatchAsync once instead of AddAsync per envelope.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_with_two_envelopes_should_call_add_batch_once()
    {
        IReadOnlyList<OutboxEnvelope> envelopes =
        [
            new() { Id = Guid.NewGuid(), ContractName = "tests.events.sample", ContractVersion = 1, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Status = OutboxStatus.Pending, AttemptCount = 0 },
            new() { Id = Guid.NewGuid(), ContractName = "tests.events.sample", ContractVersion = 1, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Status = OutboxStatus.Pending, AttemptCount = 0 }
        ];
        var factory = new FixedFanOutFactory(envelopes);
        var store = new CountingStore();
        var outbox = new Outbox(store, factory);

        await outbox.EnqueueAsync(OutboxEnqueueItem<TestEvent>.From(new TestEvent())).ConfigureAwait(false);

        store.AddBatchCallCount.Should().Be(1);
        store.AddCallCount.Should().Be(0);
        store.LastBatchSize.Should().Be(2);
    }

    /// <summary>
    ///     Verifies the transactional writer calls AddBatchAsync once instead of AddAsync per envelope.
    /// </summary>
    [Fact]
    public async Task Transactional_enqueue_with_two_envelopes_should_call_add_batch_once()
    {
        IReadOnlyList<OutboxEnvelope> envelopes =
        [
            new() { Id = Guid.NewGuid(), ContractName = "tests.events.sample", ContractVersion = 1, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Status = OutboxStatus.Pending, AttemptCount = 0 },
            new() { Id = Guid.NewGuid(), ContractName = "tests.events.sample", ContractVersion = 1, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, Status = OutboxStatus.Pending, AttemptCount = 0 }
        ];
        var factory = new FixedFanOutFactory(envelopes);
        var store = new CountingTransactionalStore();
        var outbox = new StoreBoundTransactionalOutbox(store, factory);

        await outbox.EnqueueAsync(OutboxEnqueueItem<TestEvent>.From(new TestEvent())).ConfigureAwait(false);

        store.AddBatchCallCount.Should().Be(1);
        store.AddCallCount.Should().Be(0);
        store.LastBatchSize.Should().Be(2);
    }

    private sealed record TestEvent;

    private sealed class FixedFanOutFactory : IOutboxEnvelopeFactory
    {
        private readonly IReadOnlyList<OutboxEnvelope> _envelopes;

        public FixedFanOutFactory(IReadOnlyList<OutboxEnvelope> envelopes)
        {
            ArgumentNullException.ThrowIfNull(envelopes);

            _envelopes = envelopes;
        }

        public Task<OutboxEnvelope> CreateAsync<TEvent>(OutboxEnqueueItem<TEvent> item, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            throw new NotSupportedException();
        }

        public Task<OutboxEnvelope> CreateAsync(OutboxEnqueueItem item, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync<TEvent>(OutboxEnqueueItem<TEvent> item, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            return Task.FromResult(_envelopes);
        }

        public Task<IReadOnlyList<OutboxEnvelope>> CreateManyAsync(OutboxEnqueueItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_envelopes);
        }

        public Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync<TEvent>(IReadOnlyList<OutboxEnqueueItem<TEvent>> items, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<OutboxEnvelope>> CreateBatchAsync(IReadOnlyList<OutboxEnqueueItem> items, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CountingStore : IOutboxStore
    {
        public int AddCallCount { get; private set; }

        public int AddBatchCallCount { get; private set; }

        public int LastBatchSize { get; private set; }

        public Task<OutboxAppendResult> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            return Task.FromResult(new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued));
        }

        public Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            AddBatchCallCount++;
            LastBatchSize = envelopes.Count;
            IReadOnlyList<OutboxAppendResult> results = envelopes.Select(envelope => new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued)).ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class CountingTransactionalStore : ITransactionalOutboxStore
    {
        public int AddCallCount { get; private set; }

        public int AddBatchCallCount { get; private set; }

        public int LastBatchSize { get; private set; }

        public Task<OutboxAppendResult> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            AddCallCount++;
            return Task.FromResult(new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued));
        }

        public Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
        {
            AddBatchCallCount++;
            LastBatchSize = envelopes.Count;
            IReadOnlyList<OutboxAppendResult> results = envelopes.Select(envelope => new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued)).ToList();
            return Task.FromResult(results);
        }
    }
}
