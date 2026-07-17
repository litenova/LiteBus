using System.Text.Json;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore;

/// <summary>
///     Verifies transactional EF writers resolve pending, tracked, and persisted idempotent submissions.
/// </summary>
public sealed class TransactionalDurableWriterIdempotencyTests
{
    /// <summary>
    ///     Verifies outbox batch staging and duplicate resolution across pending, tracked, and persisted rows.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TransactionalOutbox_ShouldResolveEveryIdempotencySource()
    {
        var (context, writer) = CreateOutbox();
        await using (context.ConfigureAwait(false))
        {
            var empty = await writer.EnqueueBatchAsync([]).ConfigureAwait(false);
            var metadata = CreateOutboxMetadata("outbox-key");
            OutboxEnqueueItem[] items =
            [
                OutboxEnqueueItem.From(new OrderSubmitted(Guid.NewGuid()), typeof(OrderSubmitted), metadata),
                OutboxEnqueueItem.From(new OrderSubmitted(Guid.NewGuid()), typeof(OrderSubmitted), metadata)
            ];

            var pending = await writer.EnqueueBatchAsync(items).ConfigureAwait(false);

            empty.Should().BeEmpty();
            pending.Should().HaveCount(2);
            pending[0].Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
            pending[1].Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            pending[1].Id.Should().Be(pending[0].Id);
            (await context.SaveChangesAsync().ConfigureAwait(false)).Should().Be(1);

            var tracked = await writer.EnqueueAsync(
                OutboxEnqueueItem.From(new OrderSubmitted(Guid.NewGuid()), typeof(OrderSubmitted), metadata)).ConfigureAwait(false);
            tracked.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            tracked.Id.Should().Be(pending[0].Id);

            context.ChangeTracker.Clear();
            var persisted = await writer.EnqueueAsync(
                OutboxEnqueueItem.From(new OrderSubmitted(Guid.NewGuid()), typeof(OrderSubmitted), metadata)).ConfigureAwait(false);
            persisted.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            persisted.Id.Should().Be(pending[0].Id);

            context.ChangeTracker.Clear();
            var byId = await writer.EnqueueAsync(
                OutboxEnqueueItem.From(
                    new OrderSubmitted(Guid.NewGuid()),
                    typeof(OrderSubmitted),
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Identity = new MessageIdentity.Supplied(pending[0].Id)
                    })).ConfigureAwait(false);
            byId.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
            byId.Id.Should().Be(pending[0].Id);
        }
    }

    /// <summary>
    ///     Verifies inbox batch staging and duplicate resolution across pending, tracked, and persisted rows.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TransactionalInbox_ShouldResolveEveryIdempotencySource()
    {
        var (context, writer) = CreateInbox();
        await using (context.ConfigureAwait(false))
        {
            var empty = await writer.AcceptBatchAsync([]).ConfigureAwait(false);
            var metadata = CreateInboxMetadata("inbox-key");
            InboxAcceptItem[] items =
            [
                InboxAcceptItem.From(new SubmitOrder(Guid.NewGuid()), typeof(SubmitOrder), metadata),
                InboxAcceptItem.From(new SubmitOrder(Guid.NewGuid()), typeof(SubmitOrder), metadata)
            ];

            var pending = await writer.AcceptBatchAsync(items).ConfigureAwait(false);

            empty.Should().BeEmpty();
            pending.Should().HaveCount(2);
            pending[0].Outcome.Should().Be(InboxAcceptOutcome.Accepted);
            pending[1].Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
            pending[1].Id.Should().Be(pending[0].Id);
            (await context.SaveChangesAsync().ConfigureAwait(false)).Should().Be(1);

            var tracked = await writer.AcceptAsync(new InboxAcceptItem<SubmitOrder>
            {
                Message = new SubmitOrder(Guid.NewGuid()),
                Metadata = metadata
            }).ConfigureAwait(false);
            tracked.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
            tracked.Id.Should().Be(pending[0].Id);

            context.ChangeTracker.Clear();
            var persisted = await writer.AcceptAsync(new InboxAcceptItem<SubmitOrder>
            {
                Message = new SubmitOrder(Guid.NewGuid()),
                Metadata = metadata
            }).ConfigureAwait(false);
            persisted.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
            persisted.Id.Should().Be(pending[0].Id);

            context.ChangeTracker.Clear();
            var byId = await writer.AcceptAsync(new InboxAcceptItem<SubmitOrder>
            {
                Message = new SubmitOrder(Guid.NewGuid()),
                Metadata = InboxAcceptMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(pending[0].Id)
                }
            }).ConfigureAwait(false);
            byId.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
            byId.Id.Should().Be(pending[0].Id);
        }
    }

    /// <summary>
    ///     Verifies transactional writers can stage strict envelopes before a context implements a durable store role.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task TransactionalWriters_WithPlainDbContext_ShouldStageWithoutLookup()
    {
        var options = new DbContextOptionsBuilder<PlainDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var context = new PlainDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var outboxInterceptor = new LiteBusOutboxSaveChangesInterceptor();
            var inboxInterceptor = new LiteBusInboxSaveChangesInterceptor();
            var serializer = new TestMessageSerializer();
            var registry = new MessageContractRegistry();
            registry.Register<OrderSubmitted>("tests.events.order-submitted");
            registry.Register<SubmitOrder>("tests.commands.submit-order");
            var outbox = new TransactionalOutbox<PlainDbContext>(
                outboxInterceptor,
                context,
                new OutboxEnvelopeFactory(registry, serializer, TimeProvider.System));
            var inbox = new TransactionalInbox<PlainDbContext>(
                inboxInterceptor,
                context,
                new InboxEnvelopeFactory(registry, serializer, TimeProvider.System));

            var outboxReceipt = await outbox.EnqueueAsync(
                OutboxEnqueueItem<OrderSubmitted>.From(
                    new OrderSubmitted(Guid.NewGuid()),
                    CreateOutboxMetadata("plain-outbox", IdempotencyConflictMode.Strict))).ConfigureAwait(false);
            var inboxReceipt = await inbox.AcceptAsync(new InboxAcceptItem<SubmitOrder>
            {
                Message = new SubmitOrder(Guid.NewGuid()),
                Metadata = CreateInboxMetadata("plain-inbox", IdempotencyConflictMode.Strict)
            }).ConfigureAwait(false);

            outboxReceipt.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
            inboxReceipt.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        }
    }

    private static (TestOutboxDbContext Context, TransactionalOutbox<TestOutboxDbContext> Writer) CreateOutbox()
    {
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusOutboxInterceptor(interceptor)
            .Options;
        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();
        var registry = new MessageContractRegistry();
        registry.Register<OrderSubmitted>("tests.events.order-submitted");
        var writer = new TransactionalOutbox<TestOutboxDbContext>(
            interceptor,
            context,
            new OutboxEnvelopeFactory(registry, new TestMessageSerializer(), TimeProvider.System));
        return (context, writer);
    }

    private static (TestInboxDbContext Context, TransactionalInbox<TestInboxDbContext> Writer) CreateInbox()
    {
        var interceptor = new LiteBusInboxSaveChangesInterceptor();
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddLiteBusInboxInterceptor(interceptor)
            .Options;
        var context = new TestInboxDbContext(options);
        context.Database.EnsureCreated();
        var registry = new MessageContractRegistry();
        registry.Register<SubmitOrder>("tests.commands.submit-order");
        var writer = new TransactionalInbox<TestInboxDbContext>(
            interceptor,
            context,
            new InboxEnvelopeFactory(registry, new TestMessageSerializer(), TimeProvider.System));
        return (context, writer);
    }

    private static OutboxEnqueueMetadata CreateOutboxMetadata(
        string key,
        IdempotencyConflictMode conflictMode = IdempotencyConflictMode.ReturnExisting)
    {
        return OutboxEnqueueMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed(key, conflictMode),
            Tenant = new TenantScope.Isolated("tenant-a")
        };
    }

    private static InboxAcceptMetadata CreateInboxMetadata(
        string key,
        IdempotencyConflictMode conflictMode = IdempotencyConflictMode.ReturnExisting)
    {
        return InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed(key, conflictMode),
            Tenant = new TenantScope.Isolated("tenant-a")
        };
    }

    private sealed record OrderSubmitted(Guid OrderId);

    private sealed record SubmitOrder(Guid OrderId);

    private sealed class TestMessageSerializer : IMessageSerializer
    {
        public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            return Task.FromResult(JsonSerializer.Serialize(message));
        }

        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(JsonSerializer.Deserialize(payload, messageType)!);
        }
    }

    private sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
    {
        public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration(modelBuilder);
        }
    }

    private sealed class TestInboxDbContext : DbContext, IInboxDbContext
    {
        public TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration(modelBuilder);
        }
    }

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext(DbContextOptions<PlainDbContext> options)
            : base(options)
        {
        }
    }
}
