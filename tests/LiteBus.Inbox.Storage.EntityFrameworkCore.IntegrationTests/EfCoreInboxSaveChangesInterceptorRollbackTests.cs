using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Inbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies transactional inbox interceptor rollback behavior against PostgreSQL.
/// </summary>
public sealed class EfCoreInboxSaveChangesInterceptorRollbackTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxSaveChangesInterceptorRollbackTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms rolled-back <c>SaveChanges</c> does not persist queued inbox rows.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ShouldNotPersistInboxMessage_WhenTransactionRollsBack()
    {
        var storeOptions = await CreateInboxTableAsync().ConfigureAwait(false);
        var interceptor = new LiteBusInboxSaveChangesInterceptor();

        await using var context = CreateContext(storeOptions, interceptor);
        var envelope = CreateEnvelope();

        await using var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
        interceptor.Enqueue(context, envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.RollbackAsync().ConfigureAwait(false);

        await using var verificationContext = CreateContext(storeOptions);
        var storedCount = await verificationContext.InboxMessages
            .CountAsync(message => message.Id == envelope.Id)
            .ConfigureAwait(false);

        storedCount.Should().Be(0);
    }

    private async Task<EfCoreInboxStoreOptions> CreateInboxTableAsync()
    {
        var options = new EfCoreInboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"inbox_ef_rollback_{Guid.NewGuid():N}"
        };

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(_fixture.ConnectionString);
        await PostgreSqlInboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlInboxStoreOptions
            {
                SchemaName = options.SchemaName,
                TableName = options.TableName,
                ValidateSchemaCreationOnStartup = false
            }).ConfigureAwait(false);

        return options;
    }

    private IntegrationInboxRollbackDbContext CreateContext(
        EfCoreInboxStoreOptions storeOptions,
        LiteBusInboxSaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxRollbackDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor is not null)
        {
            builder.AddLiteBusInboxInterceptor(interceptor);
        }

        return new IntegrationInboxRollbackDbContext(builder.Options, storeOptions);
    }

    private static InboxEnvelope CreateEnvelope()
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.commands.submit",
            ContractVersion = 1,
            Payload = """{"orderId":"123"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        };
    }

    private sealed class IntegrationInboxRollbackDbContext : DbContext, IInboxDbContext
    {
        private readonly EfCoreInboxStoreOptions _storeOptions;

        public IntegrationInboxRollbackDbContext(
            DbContextOptions<IntegrationInboxRollbackDbContext> options,
            EfCoreInboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, LiteBus.Storage.EntityFrameworkCore.EfCoreStorageProvider.PostgreSql);
        }
    }
}
