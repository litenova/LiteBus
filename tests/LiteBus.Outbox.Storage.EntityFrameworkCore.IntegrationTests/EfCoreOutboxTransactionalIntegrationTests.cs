using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies domain state and outbox rows commit or roll back together through
///     <see cref="EfCoreOutboxStore.UseExistingDbContext{TContext}(TContext)" />.
/// </summary>
public sealed class EfCoreOutboxTransactionalIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxTransactionalIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxTransactionalIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a rolled-back transaction removes both domain and outbox rows.
    /// </summary>
    [Fact]
    public async Task UseExistingDbContext_ShouldRollbackDomainAndOutboxTogether()
    {
        var (storeOptions, ordersTableName) = await CreateTablesAsync();
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();

        await using (var context = CreateTransactionalContext(storeOptions, ordersTableName))
        {
            var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), storeOptions);
            var transactionalStore = store.UseExistingDbContext(context);

            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Orders.Add(new DomainOrderEntity { OrderId = orderId, Amount = 42m });
            await transactionalStore.AddAsync(envelope);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verificationContext = CreateTransactionalContext(storeOptions, ordersTableName);

        (await verificationContext.Orders.CountAsync(order => order.OrderId == orderId))
            .Should().Be(0);

        (await verificationContext.OutboxMessages.CountAsync(message => message.Id == envelope.Id))
            .Should().Be(0);
    }

    /// <summary>
    ///     Confirms a committed transaction persists both domain and outbox rows.
    /// </summary>
    [Fact]
    public async Task UseExistingDbContext_ShouldCommitDomainAndOutboxTogether()
    {
        var (storeOptions, ordersTableName) = await CreateTablesAsync();
        var orderId = Guid.NewGuid();
        var envelope = CreateEnvelope();

        await using (var context = CreateTransactionalContext(storeOptions, ordersTableName))
        {
            var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(context), storeOptions);
            var transactionalStore = store.UseExistingDbContext(context);

            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Orders.Add(new DomainOrderEntity { OrderId = orderId, Amount = 99m });
            await transactionalStore.AddAsync(envelope);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationContext = CreateTransactionalContext(storeOptions, ordersTableName);

        var order = await verificationContext.Orders.SingleOrDefaultAsync(entity => entity.OrderId == orderId)
            ;

        var message = await verificationContext.OutboxMessages.SingleOrDefaultAsync(entity => entity.Id == envelope.Id)
            ;

        order.Should().NotBeNull();
        order!.Amount.Should().Be(99m);
        message.Should().NotBeNull();
        message!.ContractName.Should().Be(envelope.ContractName);
    }

    /// <summary>
    ///     Creates outbox and domain tables for one test run.
    /// </summary>
    /// <returns>The outbox store options and domain table name.</returns>
    private async Task<(EfCoreOutboxStoreOptions StoreOptions, string OrdersTableName)> CreateTablesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var storeOptions = new EfCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"outbox_ef_tx_{suffix}"
        };

        var ordersTableName = $"orders_ef_tx_{suffix}";

        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);

        await PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlOutboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            });

        await using var context = CreateTransactionalContext(storeOptions, ordersTableName);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             CREATE TABLE IF NOT EXISTS "{storeOptions.SchemaName}"."{ordersTableName}" (
                 order_id uuid NOT NULL PRIMARY KEY,
                 amount numeric NOT NULL);
             """);

        return (storeOptions, ordersTableName);
    }

    /// <summary>
    ///     Creates a database context that includes domain and outbox sets.
    /// </summary>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <returns>The configured context.</returns>
    private TransactionalOutboxDbContext CreateTransactionalContext(
        EfCoreOutboxStoreOptions storeOptions,
        string ordersTableName)
    {
        var builder = new DbContextOptionsBuilder<TransactionalOutboxDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        return new TransactionalOutboxDbContext(builder.Options, storeOptions, ordersTableName);
    }

    /// <summary>
    ///     Creates a sample outbox envelope.
    /// </summary>
    /// <returns>The envelope used by tests.</returns>
    private static OutboxEnvelope CreateEnvelope()
    {
        return new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"456"}""",
            Topic = "orders",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0
        };
    }

    /// <summary>
    ///     A minimal domain row used to prove atomic commit with outbox messages.
    /// </summary>
    private sealed class DomainOrderEntity
    {
        /// <summary>
        ///     Gets or sets the order identifier.
        /// </summary>
        public Guid OrderId { get; set; }

        /// <summary>
        ///     Gets or sets the order amount.
        /// </summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    ///     Database context that maps both domain orders and outbox messages for transactional tests.
    /// </summary>
    private sealed class TransactionalOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     The domain orders table name.
        /// </summary>
        private readonly string _ordersTableName;

        /// <summary>
        ///     The outbox store options used for schema mapping.
        /// </summary>
        private readonly EfCoreOutboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TransactionalOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The outbox store options.</param>
        /// <param name="ordersTableName">The domain orders table name.</param>
        public TransactionalOutboxDbContext(
            DbContextOptions<TransactionalOutboxDbContext> options,
            EfCoreOutboxStoreOptions storeOptions,
            string ordersTableName)
            : base(options)
        {
            _storeOptions = storeOptions;
            _ordersTableName = ordersTableName;
        }

        /// <summary>
        ///     Gets the domain orders tracked by the context.
        /// </summary>
        public DbSet<DomainOrderEntity> Orders => Set<DomainOrderEntity>();

        /// <inheritdoc />
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);

            modelBuilder.Entity<DomainOrderEntity>(entity =>
            {
                entity.ToTable(_ordersTableName, _storeOptions.SchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.Amount).HasColumnName("amount").HasColumnType("numeric");
            });
        }
    }
}