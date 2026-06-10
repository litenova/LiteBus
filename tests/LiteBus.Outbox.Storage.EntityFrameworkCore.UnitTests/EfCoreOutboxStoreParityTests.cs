using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies EF Core outbox store parity with PostgreSQL and in-memory semantics.
/// </summary>
public sealed class EfCoreOutboxStoreParityTests
{
    /// <summary>
    ///     Verifies leasing honors the tenant filter on the lease request.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_FiltersByTenantId()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var store = CreateStore(databaseName);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        await store.AddAsync(CreatePendingEnvelope("tenant-a", now));
        await store.AddAsync(CreatePendingEnvelope("tenant-b", now));

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-b"
        });

        leased.Should().ContainSingle();
        leased[0].TenantId.Should().Be("tenant-b");
    }

    /// <summary>
    ///     Verifies stale publishing rows with a null lease expiry can be reclaimed after one lease duration.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ReclaimsStaleNullLeaseAfterLeaseDuration()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var store = CreateStore(databaseName);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var envelope = CreatePendingEnvelope("tenant-a", now.AddHours(-2));

        await store.AddAsync(envelope);
        await SeedPublishingWithNullLeaseAsync(databaseName, envelope.Id, now.AddHours(-1));

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "recovery-publisher",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(30),
            TenantId = "tenant-a"
        });

        leased.Should().ContainSingle();
        leased[0].LeaseOwner.Should().Be("recovery-publisher");
        leased[0].AttemptCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies duplicate identifiers within one batch resolve to the same stored envelope.
    /// </summary>
    [Fact]
    public async Task AddBatchAsync_DeduplicatesWithinBatchByIdAndIdempotencyKey()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var store = CreateStore(databaseName);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var firstId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();

        var batch = await store.AddBatchAsync([
            CreatePendingEnvelope("tenant-a", now) with { Id = firstId, IdempotencyKey = "batch-key" },
            CreatePendingEnvelope("tenant-a", now) with { Id = duplicateId, IdempotencyKey = "batch-key" }
        ]);

        batch.Should().HaveCount(2);
        batch[0].Id.Should().Be(firstId);
        batch[1].Id.Should().Be(firstId);

        await using var context = CreateContext(databaseName);
        (await context.OutboxMessages.CountAsync()).Should().Be(1);
    }

    /// <summary>
    ///     Verifies terminal persist reports skipped envelopes when the lease guard no longer matches.
    /// </summary>
    [Fact]
    public async Task PersistAsync_ReturnsSkippedCountWhenLeaseIsLost()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var store = CreateStore(databaseName);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var envelope = CreatePendingEnvelope("tenant-a", now);

        await store.AddAsync(envelope);
        var leased = (await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        })).Single();

        var result = await store.PersistAsync([
            leased.AsPublished() with { LeaseOwner = "other-worker" }
        ]);

        result.AppliedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
    }

    /// <summary>
    ///     Creates an EF Core outbox store backed by one isolated in-memory database.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <returns>The outbox store.</returns>
    private static EfCoreOutboxStore CreateStore(string databaseName) =>
        new(_ => Task.FromResult<IOutboxDbContext>(CreateContext(databaseName)), new EfCoreOutboxStoreOptions());

    /// <summary>
    ///     Creates a configured outbox database context.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <returns>The database context.</returns>
    private static TestOutboxDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Seeds one publishing row with a null lease expiry for stale reclaim tests.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <param name="messageId">The message identifier to update.</param>
    /// <param name="createdAt">The created timestamp used for stale cutoff evaluation.</param>
    /// <returns>A task that represents the asynchronous seed operation.</returns>
    private static async Task SeedPublishingWithNullLeaseAsync(string databaseName, Guid messageId, DateTimeOffset createdAt)
    {
        await using var context = CreateContext(databaseName);
        var entity = await context.OutboxMessages.SingleAsync(message => message.Id == messageId);
        entity.Status = OutboxStatus.Publishing;
        entity.AttemptCount = 1;
        entity.LeaseOwner = "stale-publisher";
        entity.LeaseExpiresAt = null;
        entity.CreatedAt = createdAt;
        await context.SaveChangesAsync();
    }

    /// <summary>
    ///     Creates one pending outbox envelope for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="createdAt">The created timestamp.</param>
    /// <returns>The outbox envelope.</returns>
    private static OutboxEnvelope CreatePendingEnvelope(string tenantId, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.shipped",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = OutboxStatus.Pending,
            TenantId = tenantId
        };

    /// <summary>
    ///     Test database context that exposes outbox messages.
    /// </summary>
    private sealed class TestOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TestOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
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
