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
///     Verifies transactional outbox staging compares handler targeting under strict idempotency.
/// </summary>
public sealed class TransactionalOutboxStrictHandlerTests
{
    /// <summary>
    ///     Confirms a strict resubmission that differs only in handler targeting throws a conflict.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_WithStrictSameIdDifferentHandler_ShouldThrowConflict()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<StrictHandlerOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        var context = new StrictHandlerOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var registry = new MessageContractRegistry();
            registry.Register<OrderSubmittedEvent>("orders.events.submitted", 1);

            var transactionalOutbox = new TransactionalOutbox<StrictHandlerOutboxDbContext>(
                interceptor,
                context,
                new OutboxEnvelopeFactory(registry, new SynchronousMessageSerializer(), TimeProvider.System));

            var messageId = Guid.NewGuid();
            var submitted = new OrderSubmittedEvent { OrderId = Guid.NewGuid() };

            context.OutboxMessages.Add(new OutboxMessageEntity
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = JsonSerializer.Serialize(submitted),
                Topic = "orders",
                Handler = "Tests.FirstHandler",
                CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                TenantId = string.Empty,
                IdempotencyKey = "handler-strict-key"
            });

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var action = () => transactionalOutbox.EnqueueAsync(OutboxEnqueueItem.From(
                submitted,
                typeof(OrderSubmittedEvent),
                OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Idempotency = new Idempotency.Keyed("handler-strict-key", IdempotencyConflictMode.Strict),
                    Target = new PublicationTarget.Topic("orders")
                }));

            await action.Should().ThrowAsync<IdempotencyConflictException>().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Confirms a strict resubmission with identical handler targeting reports the stored receipt.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_WithStrictSameIdSameHandler_ShouldReportAlreadyEnqueued()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();

        var options = new DbContextOptionsBuilder<StrictHandlerOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;

        var context = new StrictHandlerOutboxDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

            var registry = new MessageContractRegistry();
            registry.Register<OrderSubmittedEvent>("orders.events.submitted", 1);

            var transactionalOutbox = new TransactionalOutbox<StrictHandlerOutboxDbContext>(
                interceptor,
                context,
                new OutboxEnvelopeFactory(registry, new SynchronousMessageSerializer(), TimeProvider.System));

            var messageId = Guid.NewGuid();
            var submitted = new OrderSubmittedEvent { OrderId = Guid.NewGuid() };

            context.OutboxMessages.Add(new OutboxMessageEntity
            {
                Id = messageId,
                ContractName = "orders.events.submitted",
                ContractVersion = 1,
                Payload = JsonSerializer.Serialize(submitted),
                Topic = "orders",
                Handler = null,
                CreatedAt = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero),
                Status = OutboxStatus.Pending,
                AttemptCount = 0,
                TenantId = string.Empty,
                IdempotencyKey = "handler-strict-key"
            });

            await context.SaveChangesAsync().ConfigureAwait(false);
            context.ChangeTracker.Clear();

            var receipt = await transactionalOutbox.EnqueueAsync(OutboxEnqueueItem.From(
                submitted,
                typeof(OrderSubmittedEvent),
                OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Idempotency = new Idempotency.Keyed("handler-strict-key", IdempotencyConflictMode.Strict),
                    Target = new PublicationTarget.Topic("orders")
                })).ConfigureAwait(false);

            receipt.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        }
    }

    private sealed record OrderSubmittedEvent
    {
        public Guid OrderId { get; init; }
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

    private sealed class StrictHandlerOutboxDbContext : DbContext, IOutboxDbContext
    {
        public StrictHandlerOutboxDbContext(DbContextOptions<StrictHandlerOutboxDbContext> options)
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
