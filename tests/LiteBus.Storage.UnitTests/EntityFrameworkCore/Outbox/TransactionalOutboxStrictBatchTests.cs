using System.Text.Json;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies strict same-identifier batches fail instead of silently dropping envelopes.
/// </summary>
public sealed class TransactionalOutboxStrictBatchTests
{
    /// <summary>
    ///     Confirms a strict batch with the same identifier and different payloads throws before staging.
    /// </summary>
    [Fact]
    public async Task EnqueueBatchAsync_WithStrictSameIdDifferentPayload_ShouldThrowAndStageNothing()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<StrictBatchOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        var context = new StrictBatchOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var registry = new MessageContractRegistry();
            registry.Register<OrderSubmittedEvent>("orders.events.submitted", 1);

            var transactionalOutbox = new TransactionalOutbox<StrictBatchOutboxDbContext>(
                interceptor,
                context,
                new OutboxEnvelopeFactory(registry, new SynchronousMessageSerializer(), TimeProvider.System));

            var messageId = Guid.NewGuid();

            var action = () => transactionalOutbox.EnqueueBatchAsync([
                OutboxEnqueueItem.From(
                    new OrderSubmittedEvent { OrderId = Guid.NewGuid() },
                    typeof(OrderSubmittedEvent),
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Identity = new MessageIdentity.Supplied(messageId),
                        Idempotency = new Idempotency.Keyed("strict-batch-key", IdempotencyConflictMode.Strict)
                    }),
                OutboxEnqueueItem.From(
                    new OrderSubmittedEvent { OrderId = Guid.NewGuid() },
                    typeof(OrderSubmittedEvent),
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Identity = new MessageIdentity.Supplied(messageId),
                        Idempotency = new Idempotency.Keyed("strict-batch-key", IdempotencyConflictMode.Strict)
                    })
            ]);

            await action.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);

            (await context.OutboxMessages.CountAsync().ConfigureAwait(false)).Should().Be(0);
        }
    }

    /// <summary>
    ///     Confirms direct interceptor staging of strict duplicate identifiers fails on save instead of collapsing.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_WithStrictSameIdDifferentPayload_ShouldThrowAndPersistNothing()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<StrictBatchOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        var context = new StrictBatchOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var messageId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

            interceptor.Enqueue(context, new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            });

            interceptor.Enqueue(context, new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"2"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                IdempotencyConflictMode = IdempotencyConflictMode.Strict
            });

            var action = () => context.SaveChangesAsync();

            await action.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

            context.ChangeTracker.Clear();

            (await context.OutboxMessages.CountAsync().ConfigureAwait(false)).Should().Be(0);
        }
    }

    /// <summary>
    ///     Creates a test order submitted event record.
    /// </summary>
    private sealed record OrderSubmittedEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Serializes synchronously so enqueue and save run on the same test execution flow.
    /// </summary>
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

    /// <summary>
    ///     Test database context that exposes outbox messages.
    /// </summary>
    private sealed class StrictBatchOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StrictBatchOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public StrictBatchOutboxDbContext(DbContextOptions<StrictBatchOutboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
