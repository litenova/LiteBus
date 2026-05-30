using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Runs shared outbox store contract tests against the in-memory Entity Framework Core provider.
/// </summary>
public sealed class EfCoreOutboxStoreContractTests : OutboxStoreContractTests, IDisposable
{
    /// <summary>
    ///     The database name used to isolate this test class.
    /// </summary>
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    /// <summary>
    ///     The outbox store under test.
    /// </summary>
    private EfCoreOutboxStore? _store;

    /// <summary>
    ///     Releases resources held by the current test class.
    /// </summary>
    public void Dispose()
    {
        _store = null;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        var context = CreateContext();
        _store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), new EfCoreOutboxStoreOptions());
        return new OutboxStoreContracts(_store, _store, _store);
    }

    /// <summary>
    ///     Creates a new in-memory database context configured for outbox contract tests.
    /// </summary>
    /// <returns>The database context.</returns>
    private TestOutboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var context = new TestOutboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

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
