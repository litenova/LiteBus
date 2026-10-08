using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore;

/// <summary>
///     Verifies lease release on the Entity Framework Core inbox and outbox stores.
/// </summary>
public sealed class EfCoreStoreLeaseReleaseTests
{
    /// <summary>
    ///     Confirms an outbox release with matching owner and generation returns the row to the queue.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenOwnerAndGenerationMatch_ShouldReturnRowToQueue()
    {
        using var context = CreateOutboxContext();
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(context),
            new EntityFrameworkCoreOutboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingOutbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = context.OutboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(OutboxStatus.Pending);
        row.AttemptCount.Should().Be(0);
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        var leasedAgain = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leasedAgain.Should().ContainSingle(envelope => envelope.Id == messageId);
        leasedAgain[0].LeaseOwner.Should().Be("worker-2");

        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms an outbox release with a wrong owner or stale generation fails and leaves the row leased.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenOwnerOrGenerationMismatch_ShouldReturnFalseAndChangeNothing()
    {
        using var context = CreateOutboxContext();
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(context),
            new EntityFrameworkCoreOutboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingOutbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();

        var wrongOwner = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-2", leased[0].LeaseGeneration)).ConfigureAwait(false);
        wrongOwner.Should().BeFalse();

        var staleGeneration = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration + 1)).ConfigureAwait(false);
        staleGeneration.Should().BeFalse();

        var row = context.OutboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(OutboxStatus.Publishing);
        row.LeaseOwner.Should().Be("worker-1");
        row.LeaseGeneration.Should().Be(leased[0].LeaseGeneration);
        row.AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);
        released.Should().BeTrue();

        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms an outbox release of a row that carries an error yields failed instead of pending.
    /// </summary>
    [Fact]
    public async Task OutboxReleaseLeaseAsync_WhenRowCarriesError_ShouldYieldFailed()
    {
        using var context = CreateOutboxContext();
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(context),
            new EntityFrameworkCoreOutboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 2,
            Status = OutboxStatus.Publishing,
            LeaseOwner = "worker-1",
            LeaseGeneration = 2,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = "dispatcher fault"
        }).ConfigureAwait(false);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", 2)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = context.OutboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(OutboxStatus.Failed);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("dispatcher fault");
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms an inbox release with matching owner and generation returns the row to the queue.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenOwnerAndGenerationMatch_ShouldReturnRowToQueue()
    {
        using var context = CreateInboxContext();
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(context),
            new EntityFrameworkCoreInboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingInbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();
        leased[0].AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = context.InboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(InboxStatus.Pending);
        row.AttemptCount.Should().Be(0);
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        var leasedAgain = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leasedAgain.Should().ContainSingle(envelope => envelope.Id == messageId);
        leasedAgain[0].LeaseOwner.Should().Be("worker-2");

        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms an inbox release with a wrong owner or stale generation fails and leaves the row leased.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenOwnerOrGenerationMismatch_ShouldReturnFalseAndChangeNothing()
    {
        using var context = CreateInboxContext();
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(context),
            new EntityFrameworkCoreInboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(CreatePendingInbox(messageId, now)).ConfigureAwait(false);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        leased.Should().ContainSingle();

        var wrongOwner = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-2", leased[0].LeaseGeneration)).ConfigureAwait(false);
        wrongOwner.Should().BeFalse();

        var staleGeneration = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration + 1)).ConfigureAwait(false);
        staleGeneration.Should().BeFalse();

        var row = context.InboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(InboxStatus.Processing);
        row.LeaseOwner.Should().Be("worker-1");
        row.LeaseGeneration.Should().Be(leased[0].LeaseGeneration);
        row.AttemptCount.Should().Be(1);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", leased[0].LeaseGeneration)).ConfigureAwait(false);
        released.Should().BeTrue();

        await store.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms an inbox release of a row that carries an error yields failed instead of pending.
    /// </summary>
    [Fact]
    public async Task InboxReleaseLeaseAsync_WhenRowCarriesError_ShouldYieldFailed()
    {
        using var context = CreateInboxContext();
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(context),
            new EntityFrameworkCoreInboxStoreOptions());
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 2,
            Status = InboxStatus.Processing,
            LeaseOwner = "worker-1",
            LeaseGeneration = 2,
            LeaseExpiresAt = now.AddMinutes(1),
            LastError = "handler fault"
        }).ConfigureAwait(false);

        var released = await store.ReleaseLeaseAsync(
            new LeaseReleaseRequest(messageId, "worker-1", 2)).ConfigureAwait(false);

        released.Should().BeTrue();

        var row = context.InboxMessages.Single(entity => entity.Id == messageId);
        row.Status.Should().Be(InboxStatus.Failed);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("handler fault");
        row.LeaseOwner.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();

        await store.DisposeAsync().ConfigureAwait(false);
    }

    private static OutboxEnvelope CreatePendingOutbox(Guid messageId, DateTimeOffset now)
    {
        return new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        };
    }

    private static InboxEnvelope CreatePendingInbox(Guid messageId, DateTimeOffset now)
    {
        return new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.release",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        };
    }

    private static TestOutboxDbContext CreateOutboxContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static TestInboxDbContext CreateInboxContext()
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var context = new TestInboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
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
