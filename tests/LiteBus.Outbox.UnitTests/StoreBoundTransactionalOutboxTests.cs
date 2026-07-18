using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies <see cref="StoreBoundTransactionalOutbox" /> writes through a bound store only.
/// </summary>
public sealed class StoreBoundTransactionalOutboxTests
{
    /// <summary>
    ///     Confirms enqueue calls the bound transactional store and returns a receipt.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_write_to_bound_store_only()
    {
        var store = new RecordingTransactionalOutboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<TestEvent>("orders.events.submitted");
        var serializer = new SystemTextJsonMessageSerializer();
        var factory = new OutboxEnvelopeFactory(registry, serializer, TimeProvider.System);
        var writer = new StoreBoundTransactionalOutbox(store, factory);

        var receipt = await writer.EnqueueAsync(OutboxWriterTestFactory.Item(new TestEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        store.AddCalls.Should().Be(1);
        store.LastEnvelope.Should().NotBeNull();
        receipt.Id.Should().Be(store.LastEnvelope!.Id);
        receipt.Contract.Name.Should().Be("orders.events.submitted");
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    private sealed record TestEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Records store writes for assertions.
    /// </summary>
    private sealed class RecordingTransactionalOutboxStore : ITransactionalOutboxStore
    {
        /// <summary>
        ///     Gets the number of single-row writes observed.
        /// </summary>
        public int AddCalls { get; private set; }

        /// <summary>
        ///     Gets the last envelope passed to <see cref="AddAsync" />.
        /// </summary>
        public OutboxEnvelope? LastEnvelope { get; private set; }

        /// <inheritdoc />
        public Task<OutboxAppendResult> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            LastEnvelope = envelope;
            return Task.FromResult(new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
