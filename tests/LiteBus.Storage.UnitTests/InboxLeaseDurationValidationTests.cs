using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Inbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Storage.UnitTests;

/// <summary>
///     Verifies every inbox store rejects a zero lease duration with the same exception.
/// </summary>
public sealed class InboxLeaseDurationValidationTests
{
    /// <summary>
    ///     Confirms a zero lease duration throws <see cref="ArgumentOutOfRangeException" /> on all three stores.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenLeaseDurationIsZero_ShouldThrowArgumentOutOfRangeOnEveryStore()
    {
        var now = DateTimeOffset.UtcNow;
        var inMemory = new InMemoryInboxStore();
        var postgreSql = new PostgreSqlInboxStore(
            NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test"),
            new PostgreSqlInboxStoreOptions());
        using var dbContext = CreateInboxDbContext();
        var efCore = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(new InboxContextWrapper(dbContext)),
            new EntityFrameworkCoreInboxStoreOptions());

        var inMemoryLease = () => inMemory.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.Zero
        });
        var postgreSqlLease = () => postgreSql.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.Zero
        });
        var efCoreLease = () => efCore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.Zero
        });

        await inMemoryLease.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await postgreSqlLease.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await efCoreLease.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
        await efCore.DisposeAsync().ConfigureAwait(false);
    }

    private static TestInboxDbContext CreateInboxDbContext()
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestInboxDbContext(options);
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
