using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Shared;

/// <summary>
///     Verifies EF Core stores reject operations that require a concrete <see cref="DbContext" />.
/// </summary>
public sealed class EfCoreStoreBoundaryTests
{
    /// <summary>
    ///     Verifies the outbox store validates paging and DbContext-dependent operations at its boundary.
    /// </summary>
    [Fact]
    public async Task OutboxStore_ShouldEnforceDbContextAndQueryBoundaries()
    {
        using var dbContext = CreateOutboxDbContext();
        var wrapper = new OutboxContextWrapper(dbContext);
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(wrapper),
            new EntityFrameworkCoreOutboxStoreOptions());

        var schema = await store.GetSchemaInfoAsync().ConfigureAwait(false);
        schema.Component.Should().Be("outbox");

        var invalidPage = () => store.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 0 });
        var invalidCursor = () => store.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { Cursor = "not-a-cursor" });
        var retention = () => store.DeletePublishedOlderThanAsync(DateTimeOffset.UtcNow);
        var purge = () => store.PurgeAsync(new OutboxMessageFilter());
        var renewal = () => store.RenewLeaseAsync(CreateLeaseRenewal());
        var leasing = () => store.LeasePendingAsync(CreateOutboxLeaseRequest());
        var add = () => store.AddAsync(CreateOutboxEnvelope(OutboxStatus.Pending));

        await invalidPage.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await invalidCursor.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        await retention.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await purge.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await renewal.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await leasing.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await add.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        OutboxStatus[] terminalStatuses = [OutboxStatus.Published, OutboxStatus.Failed, OutboxStatus.DeadLettered];

        foreach (var status in terminalStatuses)
        {
            var persist = () => store.PersistAsync([CreateOutboxEnvelope(status)]);
            await persist.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        }

        var invalidOutcome = () => store.PersistAsync([CreateOutboxEnvelope((OutboxStatus)999)]);
        await invalidOutcome.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies the inbox store validates paging and DbContext-dependent operations at its boundary.
    /// </summary>
    [Fact]
    public async Task InboxStore_ShouldEnforceDbContextAndQueryBoundaries()
    {
        using var dbContext = CreateInboxDbContext();
        var wrapper = new InboxContextWrapper(dbContext);
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(wrapper),
            new EntityFrameworkCoreInboxStoreOptions());

        var schema = await store.GetSchemaInfoAsync().ConfigureAwait(false);
        schema.Component.Should().Be("inbox");

        var invalidPage = () => store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = 0 });
        var invalidCursor = () => store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { Cursor = "not-a-cursor" });
        var retention = () => store.DeleteCompletedOlderThanAsync(DateTimeOffset.UtcNow);
        var purge = () => store.PurgeAsync(new InboxMessageFilter());
        var renewal = () => store.RenewLeaseAsync(CreateLeaseRenewal());
        var leasing = () => store.LeasePendingAsync(CreateInboxLeaseRequest());
        var add = () => store.AddAsync(CreateInboxEnvelope(InboxStatus.Pending));

        await invalidPage.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await invalidCursor.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
        await retention.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await purge.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await renewal.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await leasing.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await add.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        InboxStatus[] terminalStatuses = [InboxStatus.Completed, InboxStatus.Failed, InboxStatus.DeadLettered];

        foreach (var status in terminalStatuses)
        {
            var persist = () => store.PersistAsync([CreateInboxEnvelope(status)]);
            await persist.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        }

        var invalidOutcome = () => store.PersistAsync([CreateInboxEnvelope((InboxStatus)999)]);
        await invalidOutcome.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        await store.DisposeAsync().ConfigureAwait(false);
    }

    private static LeaseRenewalRequest CreateLeaseRenewal()
    {
        return new LeaseRenewalRequest(
            Guid.NewGuid(),
            "worker-1",
            1,
            TimeSpan.FromMinutes(1),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private static OutboxLeaseRequest CreateOutboxLeaseRequest()
    {
        return new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            LeaseDuration = TimeSpan.FromMinutes(1),
            Now = DateTimeOffset.UtcNow
        };
    }

    private static InboxLeaseRequest CreateInboxLeaseRequest()
    {
        return new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            LeaseDuration = TimeSpan.FromMinutes(1),
            Now = DateTimeOffset.UtcNow
        };
    }

    private static OutboxEnvelope CreateOutboxEnvelope(OutboxStatus status)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.boundary",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = status
        };
    }

    private static InboxEnvelope CreateInboxEnvelope(InboxStatus status)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.boundary",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = status
        };
    }

    private static TestOutboxDbContext CreateOutboxDbContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestOutboxDbContext(options);
    }

    private static TestInboxDbContext CreateInboxDbContext()
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestInboxDbContext(options);
    }

    private sealed class OutboxContextWrapper : IOutboxDbContext
    {
        private readonly TestOutboxDbContext _context;

        public OutboxContextWrapper(TestOutboxDbContext context)
        {
            _context = context;
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => _context.OutboxMessages;
    }

    private sealed class InboxContextWrapper : IInboxDbContext
    {
        private readonly TestInboxDbContext _context;

        public InboxContextWrapper(TestInboxDbContext context)
        {
            _context = context;
        }

        public DbSet<InboxMessageEntity> InboxMessages => _context.InboxMessages;
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
}
