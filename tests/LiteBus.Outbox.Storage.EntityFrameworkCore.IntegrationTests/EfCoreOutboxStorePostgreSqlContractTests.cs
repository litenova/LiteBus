using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Runs shared outbox store contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreOutboxStorePostgreSqlContractTests : OutboxStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     The isolated schema used by this test class.
    /// </summary>
    private readonly string _schemaName = "outbox_ef_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    ///     The store options that point at the isolated schema.
    /// </summary>
    private readonly EfCoreOutboxStoreOptions _options;

    /// <summary>
    ///     The outbox store under test.
    /// </summary>
    private EfCoreOutboxStore? _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _options = new EfCoreOutboxStoreOptions { SchemaName = _schemaName };
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EnsureSchema();
        var context = CreateContext();
        _store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), _options);
        return new OutboxStoreContracts(_store, _store, _store);
    }

    /// <summary>
    ///     Ensures the outbox schema and table exist for this test class.
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
    private IntegrationOutboxDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        return new IntegrationOutboxDbContext(builder.Options, _options);
    }

    /// <summary>
    ///     Integration test database context that applies outbox model configuration.
    /// </summary>
    private sealed class IntegrationOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     The store options that control schema mapping.
        /// </summary>
        private readonly EfCoreOutboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntegrationOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The outbox store options.</param>
        public IntegrationOutboxDbContext(DbContextOptions<IntegrationOutboxDbContext> options, EfCoreOutboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions);
        }
    }
}
