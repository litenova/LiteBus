using LiteBus.Inbox.Abstractions;
using Microsoft.EntityFrameworkCore;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies EF Core inbox store parity with PostgreSQL and in-memory semantics.
/// </summary>
public sealed class EfCoreInboxStoreParityTests
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

        await store.AddAsync(CreatePendingEnvelope("tenant-a", now)).ConfigureAwait(true);
        await store.AddAsync(CreatePendingEnvelope("tenant-b", now)).ConfigureAwait(true);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 10,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1),
            TenantId = "tenant-a"
        });

        leased.Should().ContainSingle();
        leased[0].TenantId.Should().Be("tenant-a");
    }

    /// <summary>
    ///     Verifies stale processing rows with a null lease expiry can be reclaimed after one lease duration.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ReclaimsStaleNullLeaseAfterLeaseDuration()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var store = CreateStore(databaseName);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var envelope = CreatePendingEnvelope("tenant-a", now.AddHours(-2));

        await store.AddAsync(envelope).ConfigureAwait(true);
        await SeedProcessingWithNullLeaseAsync(databaseName, envelope.Id, now.AddHours(-1)).ConfigureAwait(true);

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "recovery-worker",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(30),
            TenantId = "tenant-a"
        });

        leased.Should().ContainSingle();
        leased[0].LeaseOwner.Should().Be("recovery-worker");
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

         var context = CreateContext(databaseName);
         await using (context.ConfigureAwait(false))
         {
        (await context.InboxMessages.CountAsync().ConfigureAwait(true)).Should().Be(1);
        }
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

        await store.AddAsync(envelope).ConfigureAwait(true);

        var leased = (await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false)).Single();

        var result = await store.PersistAsync([
            leased.AsCompleted() with { LeaseOwner = "other-worker" }
        ]);

        result.AppliedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
    }

    /// <summary>
    ///     Creates an EF Core inbox store backed by one isolated in-memory database.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <returns>The inbox store.</returns>
    private static EfCoreInboxStore CreateStore(string databaseName)
    {
        return new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(CreateContext(databaseName)), new EntityFrameworkCoreInboxStoreOptions());
    }

    /// <summary>
    ///     Creates a configured inbox database context.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <returns>The database context.</returns>
    private static TestInboxDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var context = new TestInboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    ///     Seeds one processing row with a null lease expiry for stale reclaim tests.
    /// </summary>
    /// <param name="databaseName">The isolated database name.</param>
    /// <param name="messageId">The message identifier to update.</param>
    /// <param name="createdAt">The created timestamp used for stale cutoff evaluation.</param>
    /// <returns>A task that represents the asynchronous seed operation.</returns>
    private static async Task SeedProcessingWithNullLeaseAsync(string databaseName, Guid messageId, DateTimeOffset createdAt)
    {
         var context = CreateContext(databaseName);
         await using (context.ConfigureAwait(false))
         {
        var entity = await context.InboxMessages.SingleAsync(message => message.Id == messageId).ConfigureAwait(false);
        entity.Status = InboxStatus.Processing;
        entity.AttemptCount = 1;
        entity.LeaseOwner = "stale-worker";
        entity.LeaseExpiresAt = null;
        entity.CreatedAt = createdAt;
        await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates one pending inbox envelope for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="createdAt">The created timestamp.</param>
    /// <returns>The inbox envelope.</returns>
    private static InboxEnvelope CreatePendingEnvelope(string tenantId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            TenantId = tenantId
        };
    }

    /// <summary>
    ///     Test database context that exposes inbox messages.
    /// </summary>
    private sealed class TestInboxDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TestInboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
            : base(options)
        {
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration();
        }
    }
}
