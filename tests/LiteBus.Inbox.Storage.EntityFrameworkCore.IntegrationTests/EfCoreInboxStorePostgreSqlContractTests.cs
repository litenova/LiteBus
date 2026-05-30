using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Runs shared inbox store contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreInboxStorePostgreSqlContractTests : InboxStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     The isolated schema used by this test class.
    /// </summary>
    private readonly string _schemaName = "inbox_ef_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    ///     The store options that point at the isolated schema.
    /// </summary>
    private readonly EfCoreInboxStoreOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _options = new EfCoreInboxStoreOptions { SchemaName = _schemaName };
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        EnsureSchema();
        var store = new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(CreateContext()), _options);
        return new InboxStoreRoles(store, store, store);
    }

    /// <summary>
    ///     Ensures the inbox schema and table exist for this test class.
    /// </summary>
    private void EnsureSchema()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw($"CREATE SCHEMA IF NOT EXISTS \"{_schemaName}\"");
        context.Database.EnsureCreated();
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed test database context.
    /// </summary>
    /// <returns>The database context.</returns>
    private IntegrationInboxDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        return new IntegrationInboxDbContext(builder.Options, _options);
    }

    /// <summary>
    ///     Integration test database context that applies inbox model configuration.
    /// </summary>
    private sealed class IntegrationInboxDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     The store options that control schema mapping.
        /// </summary>
        private readonly EfCoreInboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntegrationInboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The inbox store options.</param>
        public IntegrationInboxDbContext(DbContextOptions<IntegrationInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions);
        }
    }
}
