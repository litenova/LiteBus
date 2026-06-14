using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies transactional outbox interceptor behavior against PostgreSQL constraints.
/// </summary>
public sealed class EfCoreOutboxSaveChangesInterceptorAbortTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxSaveChangesInterceptorAbortTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxSaveChangesInterceptorAbortTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms duplicate idempotency keys abort the caller transaction instead of resolving idempotently.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction()
    {
        var storeOptions = await CreateOutboxTableAsync().ConfigureAwait(true);
        var interceptor = new LiteBusOutboxSaveChangesInterceptor();
        const string idempotencyKey = "duplicate-outbox-idem-key";

         var seedContext = CreateContext(storeOptions, interceptor);
         await using (seedContext.ConfigureAwait(false))
         {
        await seedContext.Database.EnsureCreatedAsync().ConfigureAwait(true);
        interceptor.Enqueue(seedContext, CreateEnvelope(idempotencyKey));
        await seedContext.SaveChangesAsync().ConfigureAwait(true);

         var context = CreateContext(storeOptions, interceptor);
         await using (context.ConfigureAwait(false))
         {
        await context.Database.EnsureCreatedAsync().ConfigureAwait(true);
         var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(true);
         await using (transaction.ConfigureAwait(false))
         {

        interceptor.Enqueue(context, CreateEnvelope(idempotencyKey) with { Id = Guid.NewGuid() });

        var act = () => context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await transaction.RollbackAsync().ConfigureAwait(true);

         var verificationContext = CreateContext(storeOptions);
         await using (verificationContext.ConfigureAwait(false))
         {
        (await verificationContext.OutboxMessages.CountAsync().ConfigureAwait(true)).Should().Be(1);
        }
        }
        }
        }
    }

    /// <summary>
    ///     Creates an isolated outbox table for one test run.
    /// </summary>
    /// <returns>The store options for the created table.</returns>
    private async Task<EntityFrameworkCoreOutboxStoreOptions> CreateOutboxTableAsync()
    {
        var options = new EntityFrameworkCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"outbox_ef_abort_{Guid.NewGuid():N}"
        };

         var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
         await using (dataSource.ConfigureAwait(false))
         {

        await PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlOutboxStoreOptions
            {
                SchemaName = options.SchemaName,
                TableName = options.TableName,
                ValidateSchemaCreationOnStartup = false
            }).ConfigureAwait(false);


        return options;
        }
    }

    /// <summary>
    ///     Creates a database context for the test.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="interceptor">The optional save-changes interceptor.</param>
    /// <returns>The configured context.</returns>
    private IntegrationOutboxAbortDbContext CreateContext(
        EntityFrameworkCoreOutboxStoreOptions storeOptions,
        LiteBusOutboxSaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxAbortDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddLiteBusOutboxInterceptor(interceptor);
        }

        return new IntegrationOutboxAbortDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Creates a sample outbox envelope.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key assigned to the envelope.</param>
    /// <returns>The envelope used by tests.</returns>
    private static OutboxEnvelope CreateEnvelope(string idempotencyKey)
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.placed",
            ContractVersion = 1,
            Payload = """{"orderId":"123"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = idempotencyKey
        };
    }

    /// <summary>
    ///     Integration database context that exposes outbox messages.
    /// </summary>
    private sealed class IntegrationOutboxAbortDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     The outbox store options used to configure the outbox table mapping.
        /// </summary>
        private readonly EntityFrameworkCoreOutboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntegrationOutboxAbortDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The outbox store options.</param>
        public IntegrationOutboxAbortDbContext(
            DbContextOptions<IntegrationOutboxAbortDbContext> options,
            EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
        }
    }
}
