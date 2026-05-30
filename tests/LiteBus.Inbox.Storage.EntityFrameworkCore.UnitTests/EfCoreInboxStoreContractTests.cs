using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Runs shared inbox store contract tests against the in-memory Entity Framework Core provider.
/// </summary>
public sealed class EfCoreInboxStoreContractTests : InboxStoreContractTests
{
    /// <summary>
    ///     The database name used to isolate each test method.
    /// </summary>
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        var store = new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(CreateContext()), new EfCoreInboxStoreOptions());
        return new InboxStoreRoles(store, store, store);
    }

    /// <summary>
    ///     Creates a new in-memory database context configured for inbox contract tests.
    /// </summary>
    /// <returns>The database context.</returns>
    private TestInboxDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestInboxDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        var context = new TestInboxDbContext(options);
        context.Database.EnsureCreated();
        return context;
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
