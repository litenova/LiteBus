using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies transactional inbox interceptor behavior against PostgreSQL constraints.
/// </summary>
public sealed class EfCoreInboxSaveChangesInterceptorAbortTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxSaveChangesInterceptorAbortTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxSaveChangesInterceptorAbortTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms duplicate idempotency keys abort the caller transaction instead of resolving idempotently.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction()
    {
        var storeOptions = await CreateInboxTableAsync();
        var interceptor = new LiteBusInboxSaveChangesInterceptor();
        const string idempotencyKey = "duplicate-idem-key";

        await using var seedContext = CreateContext(storeOptions, interceptor);
        await seedContext.Database.EnsureCreatedAsync();
        interceptor.Enqueue(seedContext, CreateEnvelope(idempotencyKey));
        await seedContext.SaveChangesAsync();

        await using var context = CreateContext(storeOptions, interceptor);
        await context.Database.EnsureCreatedAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();

        interceptor.Enqueue(context, CreateEnvelope(idempotencyKey) with { Id = Guid.NewGuid() });

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await transaction.RollbackAsync();

        await using var verificationContext = CreateContext(storeOptions);
        (await verificationContext.InboxMessages.CountAsync()).Should().Be(1);
    }

    /// <summary>
    ///     Creates an isolated inbox table for one test run.
    /// </summary>
    /// <returns>The store options for the created table.</returns>
    private async Task<EfCoreInboxStoreOptions> CreateInboxTableAsync()
    {
        var options = new EfCoreInboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"inbox_ef_abort_{Guid.NewGuid():N}"
        };

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        await PostgreSqlInboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlInboxStoreOptions
            {
                SchemaName = options.SchemaName,
                TableName = options.TableName,
                ValidateSchemaCreationOnStartup = false
            });

        return options;
    }

    /// <summary>
    ///     Creates a database context for the test.
    /// </summary>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <param name="interceptor">The optional save-changes interceptor.</param>
    /// <returns>The configured context.</returns>
    private IntegrationInboxAbortDbContext CreateContext(
        EfCoreInboxStoreOptions storeOptions,
        LiteBusInboxSaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxAbortDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddLiteBusInboxInterceptor(interceptor);
        }

        return new IntegrationInboxAbortDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Creates a sample inbox envelope.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key assigned to the envelope.</param>
    /// <returns>The envelope used by tests.</returns>
    private static InboxEnvelope CreateEnvelope(string idempotencyKey)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.submit",
            ContractVersion = 1,
            Payload = """{"orderId":"123"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        };
    }

    /// <summary>
    ///     Integration database context that exposes inbox messages and a sample business table.
    /// </summary>
    private sealed class IntegrationInboxAbortDbContext : DbContext, IInboxDbContext
    {
        /// <summary>
        ///     The inbox store options used to configure the inbox table mapping.
        /// </summary>
        private readonly EfCoreInboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntegrationInboxAbortDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The inbox store options.</param>
        public IntegrationInboxAbortDbContext(
            DbContextOptions<IntegrationInboxAbortDbContext> options,
            EfCoreInboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <inheritdoc />
        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
        }
    }
}