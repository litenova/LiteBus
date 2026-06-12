using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="StoreBoundTransactionalInbox" /> writes through a bound store only.
/// </summary>
public sealed class StoreBoundTransactionalInboxTests
{
    /// <summary>
    ///     Confirms accept calls the bound transactional store and returns a receipt.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_should_write_to_bound_store_only()
    {
        var store = new RecordingTransactionalInboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
        var serializer = new SystemTextJsonMessageSerializer();
        var factory = new InboxEnvelopeFactory(registry, serializer, TimeProvider.System);
        var writer = new StoreBoundTransactionalInbox(store, factory);

        var receipt = await writer.AcceptAsync(InboxAcceptItem<InboxTestFixtures.ShipOrderCommand>.From(
            new InboxTestFixtures.ShipOrderCommand { OrderId = Guid.NewGuid(), IdempotencyKey = "k" })).ConfigureAwait(false);

        store.AddCalls.Should().Be(1);
        store.LastEnvelope.Should().NotBeNull();
        receipt.Id.Should().Be(store.LastEnvelope!.Id);
        receipt.Contract.Name.Should().Be("orders.commands.ship");
    }

    /// <summary>
    ///     Records store writes for assertions.
    /// </summary>
    private sealed class RecordingTransactionalInboxStore : ITransactionalInboxStore
    {
        /// <summary>
        ///     Gets the number of single-row writes observed.
        /// </summary>
        public int AddCalls { get; private set; }

        /// <summary>
        ///     Gets the last envelope passed to <see cref="AddAsync" />.
        /// </summary>
        public InboxEnvelope? LastEnvelope { get; private set; }

        /// <inheritdoc />
        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            LastEnvelope = envelope;
            return Task.FromResult(envelope);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}