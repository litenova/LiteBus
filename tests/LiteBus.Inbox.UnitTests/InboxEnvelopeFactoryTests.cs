using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies <see cref="InboxEnvelopeFactory" /> produces the same envelope shape as <see cref="Inbox" />.
/// </summary>
public sealed class InboxEnvelopeFactoryTests
{
    /// <summary>
    ///     Confirms factory output matches the fields written by the auto-commit inbox writer.
    /// </summary>
    [Fact]
    public async Task CreateAsync_should_match_inbox_writer_fields()
    {
        var now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryInboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 2);
        var serializer = new SystemTextJsonMessageSerializer();
        var clock = new ManualTimeProvider(now);
        var factory = new InboxEnvelopeFactory(registry, serializer, clock);
        var inbox = InboxWriterTestFactory.Create(store, registry, serializer, clock);

        var commandId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var message = new InboxTestFixtures.ShipOrderCommand { OrderId = orderId, IdempotencyKey = "idem-1" };

        var metadata = InboxAcceptMetadata.Immediate with
        {
            Identity = new MessageIdentity.Supplied(commandId),
            Idempotency = new Idempotency.Keyed("idem-1"),
            Trace = new MessageTrace.Distributed("corr-1", "cause-1", "trace-1"),
            Tenant = new TenantScope.Isolated("tenant-1")
        };

        var item = InboxAcceptItem<InboxTestFixtures.ShipOrderCommand>.From(message, metadata);

        var envelope = await factory.CreateAsync(InboxAcceptItem.From(item));
        var receipt = await inbox.AcceptAsync(item);

        envelope.Id.Should().Be(commandId);
        envelope.ContractName.Should().Be(receipt.Contract.Name);
        envelope.ContractVersion.Should().Be(receipt.Contract.Version);
        envelope.CreatedAt.Should().Be(receipt.AcceptedAt);
        envelope.IdempotencyKey.Should().Be("idem-1");
        envelope.CorrelationId.Should().Be("corr-1");
        envelope.CausationId.Should().Be("cause-1");
        envelope.TenantId.Should().Be("tenant-1");
        envelope.TraceContext.Should().Be("trace-1");
        envelope.Status.Should().Be(InboxStatus.Pending);
        envelope.AttemptCount.Should().Be(0);
    }

    /// <summary>
    ///     Confirms payload encryption is applied when a protector is registered.
    /// </summary>
    [Fact]
    public async Task CreateAsync_should_encrypt_payload_when_protector_configured()
    {
        var registry = new MessageContractRegistry();
        registry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
        var serializer = new SystemTextJsonMessageSerializer();
        IInboxPayloadProtector protector = new PrefixProtector("enc:");
        var factory = new InboxEnvelopeFactory(registry, serializer, TimeProvider.System, protector);

        var envelope = await factory.CreateAsync(InboxAcceptItem.From(
            new InboxTestFixtures.ShipOrderCommand { OrderId = Guid.NewGuid(), IdempotencyKey = "k" }));

        envelope.Payload.Should().StartWith("enc:");
    }

    /// <summary>
    ///     Prefix-based test protector.
    /// </summary>
    private sealed class PrefixProtector : IInboxPayloadProtector
    {
        /// <summary>
        ///     The ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixProtector" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixProtector(string prefix)
        {
            _prefix = prefix;
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prefix + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ciphertext[_prefix.Length..]);
        }
    }
}