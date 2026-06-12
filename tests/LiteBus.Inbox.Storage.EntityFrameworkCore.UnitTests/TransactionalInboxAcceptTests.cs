using System.Text.Json;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies <see cref="TransactionalInbox" /> accepts typed messages through contract resolution and serialization.
/// </summary>
public sealed class TransactionalInboxAcceptTests
{
    /// <summary>
    ///     Confirms accept stages a serialized envelope with metadata before <c>SaveChanges</c>.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_should_stage_envelope_with_contract_and_metadata()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusInboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<TransactionalInboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        await using var context = new TransactionalInboxDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var registry = new MessageContractRegistry();
        registry.Register<SubmitOrderCommand>("orders.commands.submit", 2);

        var serializer = new SynchronousMessageSerializer();

        var transactionalInbox = new TransactionalInbox<TransactionalInboxDbContext>(
            interceptor,
            context,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System));

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var receipt = await transactionalInbox.AcceptAsync(
            new InboxAcceptItem<SubmitOrderCommand>
            {
                Message = new SubmitOrderCommand { OrderId = orderId },
                Metadata = InboxAcceptMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Idempotency = new Idempotency.Keyed("idem-1"),
                    Trace = new MessageTrace.Distributed(
                        "corr-1",
                        "cause-1",
                        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
                    Tenant = new TenantScope.Isolated("tenant-1")
                }
            });

        receipt.Contract.Name.Should().Be("orders.commands.submit");
        receipt.Contract.Version.Should().Be(2);

        receipt.Trace.Should().Be(new MessageTrace.Distributed(
            "corr-1",
            "cause-1",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"));

        var savedCount = await context.SaveChangesAsync();
        savedCount.Should().Be(1);

        var stored = await context.InboxMessages.SingleAsync();
        stored.Id.Should().Be(receipt.Id);
        stored.ContractName.Should().Be("orders.commands.submit");
        stored.ContractVersion.Should().Be(2);
        stored.CorrelationId.Should().Be("corr-1");
        stored.CausationId.Should().Be("cause-1");
        stored.TenantId.Should().Be("tenant-1");
        stored.IdempotencyKey.Should().Be("idem-1");
        stored.TraceContext.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        stored.Payload.Should().Contain(orderId.ToString("D"));
        stored.Status.Should().Be(InboxStatus.Pending);
    }

    /// <summary>
    ///     Confirms transactional accept encrypts payloads the same way as <see cref="IInbox" />.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_should_encrypt_payload_when_protector_configured()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusInboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<TransactionalInboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;

        await using var context = new TransactionalInboxDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var registry = new MessageContractRegistry();
        registry.Register<SubmitOrderCommand>("orders.commands.submit");
        var serializer = new SynchronousMessageSerializer();
        IInboxPayloadProtector protector = new PrefixPayloadProtector("tx-inbox:");

        var transactionalInbox = new TransactionalInbox<TransactionalInboxDbContext>(
            interceptor,
            context,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System, protector));

        await transactionalInbox.AcceptAsync(new InboxAcceptItem<SubmitOrderCommand>
        {
            Message = new SubmitOrderCommand { OrderId = Guid.NewGuid() }
        });

        await context.SaveChangesAsync();

        var stored = await context.InboxMessages.SingleAsync();
        stored.Payload.Should().StartWith("tx-inbox:");
    }

    private sealed record SubmitOrderCommand
    {
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Serializes synchronously for deterministic unit test execution.
    /// </summary>
    /// <summary>
    ///     Prefix-based test encryptor for transactional inbox tests.
    /// </summary>
    private sealed class PrefixPayloadProtector : IInboxPayloadProtector
    {
        /// <summary>
        ///     Gets the ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixPayloadProtector" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixPayloadProtector(string prefix)
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

    private sealed class SynchronousMessageSerializer : IMessageSerializer
    {
        /// <inheritdoc />
        public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            return Task.FromResult(JsonSerializer.Serialize(message));
        }

        /// <inheritdoc />
        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(JsonSerializer.Deserialize(payload, messageType)!);
        }
    }

    private sealed class TransactionalInboxDbContext : DbContext, IInboxDbContext
    {
        public TransactionalInboxDbContext(DbContextOptions<TransactionalInboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}