using System.Text.Json;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies <see cref="TransactionalOutbox" /> enqueues typed events through contract resolution and serialization.
/// </summary>
public sealed class TransactionalOutboxEnqueueTests
{
    /// <summary>
    ///     Confirms enqueue stages a serialized envelope with metadata before <c>SaveChanges</c>.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_stage_envelope_with_contract_and_metadata()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<TransactionalOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        await using var context = new TransactionalOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var registry = new MessageContractRegistry();
        registry.Register<OrderSubmittedEvent>("orders.events.submitted", 2);

        var transactionalOutbox = new TransactionalOutbox<TransactionalOutboxDbContext>(
            interceptor,
            context,
            registry,
            new SynchronousMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        var receipt = await transactionalOutbox.EnqueueAsync(
            new OrderSubmittedEvent { OrderId = orderId },
            new OutboxOptions
            {
                Id = Guid.NewGuid(),
                Topic = "orders",
                CorrelationId = "corr-1",
                CausationId = "cause-1",
                TenantId = "tenant-1",
                IdempotencyKey = "idem-1",
                TraceContext = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
            });

        receipt.ContractName.Should().Be("orders.events.submitted");
        receipt.ContractVersion.Should().Be(2);
        receipt.CorrelationId.Should().Be("corr-1");

        var savedCount = await context.SaveChangesAsync();
        savedCount.Should().Be(1);

        var stored = await context.OutboxMessages.SingleAsync();
        stored.Id.Should().Be(receipt.Id);
        stored.ContractName.Should().Be("orders.events.submitted");
        stored.ContractVersion.Should().Be(2);
        stored.Topic.Should().Be("orders");
        stored.CorrelationId.Should().Be("corr-1");
        stored.CausationId.Should().Be("cause-1");
        stored.TenantId.Should().Be("tenant-1");
        stored.IdempotencyKey.Should().Be("idem-1");
        stored.TraceContext.Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        stored.Payload.Should().Contain(orderId.ToString("D"));
        stored.Status.Should().Be(OutboxStatus.Pending);
    }

    /// <summary>
    ///     Confirms transactional enqueue encrypts payloads the same way as <see cref="IOutbox" />.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_encrypt_payload_when_protector_configured()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<TransactionalOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        await using var context = new TransactionalOutboxDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var registry = new MessageContractRegistry();
        registry.Register<OrderSubmittedEvent>("orders.events.submitted", 1);
        IOutboxPayloadProtector protector = new PrefixPayloadProtector("tx-outbox:");

        var transactionalOutbox = new TransactionalOutbox<TransactionalOutboxDbContext>(
            interceptor,
            context,
            registry,
            new SynchronousMessageSerializer(),
            TimeProvider.System,
            protector);

        await transactionalOutbox.EnqueueAsync(new OrderSubmittedEvent { OrderId = Guid.NewGuid() });
        await context.SaveChangesAsync();

        var stored = await context.OutboxMessages.SingleAsync();
        stored.Payload.Should().StartWith("tx-outbox:");
    }

    private sealed record OrderSubmittedEvent
    {
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Serializes synchronously so enqueue and save run on the same test execution flow.
    /// </summary>
    /// <summary>
    ///     Prefix-based test encryptor for transactional outbox tests.
    /// </summary>
    private sealed class PrefixPayloadProtector : IOutboxPayloadProtector
    {
        /// <summary>
        ///     Gets the ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixPayloadProtector" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixPayloadProtector(string prefix) => _prefix = prefix;

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
            => Task.FromResult(_prefix + plaintext);

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
            => Task.FromResult(ciphertext[_prefix.Length..]);
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

    private sealed class TransactionalOutboxDbContext : DbContext, IOutboxDbContext
    {
        public TransactionalOutboxDbContext(DbContextOptions<TransactionalOutboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
