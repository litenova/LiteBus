using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

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

         var context = CreateContext(storeOptions, interceptor);
         await using (context.ConfigureAwait(false))
         {
        var envelope = CreateEnvelope();

         var transaction = await context.Database.BeginTransactionAsync().ConfigureAwait(false);
         await using (transaction.ConfigureAwait(false))
         {
        interceptor.Enqueue(context, envelope);

        var savedCount = await context.SaveChangesAsync().ConfigureAwait(false);
        savedCount.Should().Be(1);

        await transaction.RollbackAsync().ConfigureAwait(false);

         var verificationContext = CreateContext(storeOptions);
         await using (verificationContext.ConfigureAwait(false))
         {

        var storedCount = await verificationContext.InboxMessages
            .CountAsync(message => message.Id == envelope.Id).ConfigureAwait(false);

        storedCount.Should().Be(0);
        }
        }
        }
    }

    private async Task<EntityFrameworkCoreInboxStoreOptions> CreateInboxTableAsync()
    {
        var options = new EntityFrameworkCoreInboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"inbox_ef_rollback_{Guid.NewGuid():N}"
        };

         var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
         await using (dataSource.ConfigureAwait(false))
         {

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
    }

    private IntegrationInboxRollbackDbContext CreateContext(
        EntityFrameworkCoreInboxStoreOptions storeOptions,
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
        private readonly EntityFrameworkCoreInboxStoreOptions _storeOptions;

        public IntegrationInboxRollbackDbContext(
            DbContextOptions<IntegrationInboxRollbackDbContext> options,
            EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
        }
    }
}
