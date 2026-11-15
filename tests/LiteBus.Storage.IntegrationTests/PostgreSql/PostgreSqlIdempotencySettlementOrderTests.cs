using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for the idempotency settlement order: the PostgreSQL idempotency store settles after the Entity Framework Core unit of work commits, and releases the key when the commit fails.
/// </summary>
public sealed class PostgreSqlIdempotencySettlementOrderTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL state a unique-constraint violation reports.
    /// </summary>
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    ///     The shared PostgreSQL container fixture.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencySettlementOrderTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlIdempotencySettlementOrderTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task SendAsync_ShouldCommitTheOrderAndCompleteTheKeyAfterTheCommit()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();

        var provider = BuildProvider(options, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}", FailHandler: false);
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(options).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        repeat.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(1);
    }

    [SkippableFact]
    public async Task SendAsync_WhenTheCommitFails_ShouldReleaseTheKeyAndThrowTheCommitException()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();

        var provider = BuildProvider(options, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        await SeedOrderAsync(Guid.NewGuid(), reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);
        counter.Runs.Should().Be(1);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().BeNull();

        var keys = await ReadStoredKeysAsync(options).ConfigureAwait(false);
        keys.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Retry_AfterAFailedCommit_ShouldRunTheHandlerAgainInsteadOfAnswering()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();

        var provider = BuildProvider(options, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        var seedId = Guid.NewGuid();
        await SeedOrderAsync(seedId, reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);

        await DeleteOrderAsync(seedId).ConfigureAwait(false);

        var retry = await mediator.TrySendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:2";

        retry.Outcome.Should().Be(MediationOutcome.Succeeded);
        retry.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(2);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(options).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);
    }

    [SkippableFact]
    public async Task SendAsync_WithAnUnmarkedStore_WhenTheCommitFails_ShouldKeepTheKeyCompleted()
    {
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();
        var store = new UnmarkedSettlementIdempotencyStore();

        var provider = BuildProviderWithStore(store, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var reference = $"conflict-{Guid.NewGuid():N}";
        var seedId = Guid.NewGuid();
        await SeedOrderAsync(seedId, reference, "seed").ConfigureAwait(false);

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), reference, FailHandler: false);
        var act = async () => await mediator.SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<DbUpdateException>()
            .Where(exception => ContainsUniqueViolation(exception)).ConfigureAwait(false);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().BeNull();

        // The store settled before the commit, so the failed commit left the completed key behind.
        var firstReceipt = $"receipt:{command.OrderId}:1";
        store.StoredKeys.Should().ContainSingle();
        var settled = store.StoredKeys.Single();
        settled.IsCompleted.Should().BeTrue();
        settled.Payload.Should().Contain(firstReceipt);

        await DeleteOrderAsync(seedId).ConfigureAwait(false);

        var retry = await mediator.TrySendAsync(command).ConfigureAwait(false);
        retry.Outcome.Should().Be(MediationOutcome.Answered);
        retry.Value.Should().Be(firstReceipt);
        counter.Runs.Should().Be(1);

        var missing = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        missing.Should().BeNull();
    }

    [SkippableFact]
    public async Task SendAsync_WhenPostCommitSettlementFails_ShouldStillSucceedWithCommittedData()
    {
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();
        var store = new ThrowingSettlementIdempotencyStore();

        var provider = BuildProviderWithStore(store, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var command = new ApplySettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}", FailHandler: false);
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        store.StoredKeys.Should().ContainSingle();
        store.StoredKeys.Single().IsCompleted.Should().BeFalse();

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Denied);
        repeat.Code.Should().Be(IdempotencyRefusalCodes.InFlight);
    }

    [SkippableFact]
    public async Task SendAsync_WithoutAUnitOfWork_ShouldCompleteTheKey()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();

        var provider = BuildProviderWithoutUnitOfWork(options, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var command = new ApplyDirectSettlementOrderCommand(Guid.NewGuid(), $"order-{Guid.NewGuid():N}");
        var receipt = await mediator.SendAsync(command).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{command.OrderId}:1";

        receipt.Should().Be(expectedReceipt);

        var stored = await LoadOrderAsync(command.OrderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Receipt.Should().Be(expectedReceipt);

        var keys = await ReadStoredKeysAsync(options).ConfigureAwait(false);
        keys.Should().ContainSingle();
        keys[0].IsCompleted.Should().BeTrue();
        keys[0].Payload.Should().Contain(expectedReceipt);

        var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        repeat.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(1);
    }

    [SkippableFact]
    public async Task SendAsync_WhenTheHandlerFails_ShouldReleaseTheKeyImmediately()
    {
        var options = CreateOptions();
        await PostgreSqlIdempotencySchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);
        var counter = new SettlementHandlerCounter();

        var provider = BuildProvider(options, counter);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var orderId = Guid.NewGuid();
        var reference = $"order-{Guid.NewGuid():N}";
        var act = async () => await mediator.SendAsync(
            new ApplySettlementOrderCommand(orderId, reference, FailHandler: true)).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        counter.Runs.Should().Be(1);

        var keys = await ReadStoredKeysAsync(options).ConfigureAwait(false);
        keys.Should().BeEmpty();

        var missing = await LoadOrderAsync(orderId).ConfigureAwait(false);
        missing.Should().BeNull();

        var retry = await mediator.TrySendAsync(
            new ApplySettlementOrderCommand(orderId, reference, FailHandler: false)).ConfigureAwait(false);
        var expectedReceipt = $"receipt:{orderId}:2";

        retry.Outcome.Should().Be(MediationOutcome.Succeeded);
        retry.Value.Should().Be(expectedReceipt);
        counter.Runs.Should().Be(2);
    }

    /// <summary>
    ///     Creates store options against a fresh table in the shared test schema.
    /// </summary>
    /// <returns>The store options.</returns>
    private static PostgreSqlIdempotencyStoreOptions CreateOptions()
    {
        return new PostgreSqlIdempotencyStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"idempotency_{Guid.NewGuid():N}"
        };
    }

    /// <summary>
    ///     Builds a provider with the PostgreSQL idempotency store and the Entity Framework Core unit of work.
    /// </summary>
    /// <param name="options">The idempotency store options.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(PostgreSqlIdempotencyStoreOptions options, SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddDbContext<SettlementOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UsePostgreSqlIdempotencyStore(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(options);
                    pg.DisableSchemaInitialization();
                });
                builder.UseUnitOfWork<SettlementOrderUnitOfWork>();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider with the given idempotency store and the Entity Framework Core unit of work.
    /// </summary>
    /// <param name="store">The idempotency store the test observes.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProviderWithStore(IIdempotencyStore store, SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        services.AddDbContext<SettlementOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UseUnitOfWork<SettlementOrderUnitOfWork>();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider with the PostgreSQL idempotency store and no unit of work.
    /// </summary>
    /// <param name="options">The idempotency store options.</param>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProviderWithoutUnitOfWork(
        PostgreSqlIdempotencyStoreOptions options,
        SettlementHandlerCounter counter)
    {
        var services = new ServiceCollection();
        services.AddDbContext<SettlementOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(counter);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                RegisterSettlementCommands(builder);
                builder.EnableIdempotency();
                builder.UsePostgreSqlIdempotencyStore(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(options);
                    pg.DisableSchemaInitialization();
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Registers the settlement-order commands, handlers, and idempotency definitions.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    private static void RegisterSettlementCommands(CommandModuleBuilder builder)
    {
        builder.Register<ApplySettlementOrderCommand>();
        builder.Register<ApplySettlementOrderCommandHandler>();
        builder.Register<ApplySettlementOrderCommandDefinition>();
        builder.Register<ApplyDirectSettlementOrderCommand>();
        builder.Register<ApplyDirectSettlementOrderCommandHandler>();
        builder.Register<ApplyDirectSettlementOrderCommandDefinition>();
    }

    /// <summary>
    ///     Creates the shared test schema when it does not exist yet.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EnsureDomainSchemaAsync()
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            $"CREATE SCHEMA IF NOT EXISTS \"{PostgreSqlTestInfrastructure.TestSchemaName}\"",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates the settlement orders table when it does not exist yet.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EnsureDomainTableAsync()
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a domain context against the shared container.
    /// </summary>
    /// <returns>The configured context.</returns>
    private SettlementOrderDbContext CreateDomainContext()
    {
        var builder = new DbContextOptionsBuilder<SettlementOrderDbContext>().UseNpgsql(_fixture.ConnectionString);

        return new SettlementOrderDbContext(builder.Options);
    }

    /// <summary>
    ///     Inserts one settlement order outside any mediation.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="reference">The order reference.</param>
    /// <param name="receipt">The receipt recorded on the row.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SeedOrderAsync(Guid orderId, string reference, string receipt)
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        context.Orders.Add(new SettlementOrderEntity { OrderId = orderId, Reference = reference, Receipt = receipt });

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes one settlement order outside any mediation.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DeleteOrderAsync(Guid orderId)
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        var stored = await context.Orders.SingleAsync(order => order.OrderId == orderId).ConfigureAwait(false);
        context.Orders.Remove(stored);

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Loads one settlement order outside any mediation.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The stored row, or <see langword="null" /> when no row carries the identifier.</returns>
    private async Task<SettlementOrderEntity?> LoadOrderAsync(Guid orderId)
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.Orders.SingleOrDefaultAsync(order => order.OrderId == orderId).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads every stored idempotency key from the table the test created.
    /// </summary>
    /// <param name="options">The store options identifying the table.</param>
    /// <returns>The stored keys and their settlement state.</returns>
    private async Task<IReadOnlyList<StoredIdempotencyKey>> ReadStoredKeysAsync(PostgreSqlIdempotencyStoreOptions options)
    {
        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var configuredConnection = connection.ConfigureAwait(false);

        var command = new NpgsqlCommand(
            $"SELECT idempotency_key, is_completed, payload FROM {PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName)}",
            connection);
        await using var configuredCommand = command.ConfigureAwait(false);

        var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        await using var configuredReader = reader.ConfigureAwait(false);

        var keys = new List<StoredIdempotencyKey>();

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            keys.Add(new StoredIdempotencyKey(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return keys;
    }

    /// <summary>
    ///     Decides whether an exception chain reports a unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><see langword="true" /> when a PostgreSQL error in the chain carries state 23505; otherwise, <see langword="false" />.</returns>
    private static bool ContainsUniqueViolation(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (current is PostgresException postgres && postgres.SqlState == UniqueViolationSqlState)
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    /// <summary>
    ///     Counts how many times the test handler actually ran.
    /// </summary>
    public sealed class SettlementHandlerCounter
    {
        /// <summary>
        ///     The number of handler runs recorded so far.
        /// </summary>
        private int _runs;

        /// <summary>
        ///     Gets the number of handler runs recorded so far.
        /// </summary>
        public int Runs => _runs;

        /// <summary>
        ///     Records one handler run.
        /// </summary>
        /// <returns>The one-based run number.</returns>
        public int NextRun()
        {
            return Interlocked.Increment(ref _runs);
        }
    }

    /// <summary>
    ///     One settlement order row written by the test handlers.
    /// </summary>
    public sealed class SettlementOrderEntity
    {
        /// <summary>
        ///     Gets or sets the order identifier.
        /// </summary>
        public Guid OrderId { get; set; }

        /// <summary>
        ///     Gets or sets the order reference, which carries a unique index.
        /// </summary>
        public string Reference { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the receipt the handler run recorded.
        /// </summary>
        public string Receipt { get; set; } = string.Empty;
    }

    /// <summary>
    ///     The Entity Framework Core context the settlement-order tests stage and verify rows through.
    /// </summary>
    public sealed class SettlementOrderDbContext : DbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SettlementOrderDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public SettlementOrderDbContext(DbContextOptions<SettlementOrderDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        ///     Gets the settlement orders tracked by the context.
        /// </summary>
        public DbSet<SettlementOrderEntity> Orders => Set<SettlementOrderEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SettlementOrderEntity>(entity =>
            {
                entity.ToTable("settlement_orders", PostgreSqlTestInfrastructure.TestSchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.Reference).HasColumnName("reference").IsRequired();
                entity.HasIndex(order => order.Reference).IsUnique();
                entity.Property(order => order.Receipt).HasColumnName("receipt");
            });
        }
    }

    /// <summary>
    ///     Commits the scoped settlement-order context as the mediation unit of work.
    /// </summary>
    public sealed class SettlementOrderUnitOfWork : IUnitOfWork
    {
        /// <summary>
        ///     The scoped context the handler staged its row on.
        /// </summary>
        private readonly SettlementOrderDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SettlementOrderUnitOfWork" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler staged its row on.</param>
        public SettlementOrderUnitOfWork(SettlementOrderDbContext context)
        {
            _context = context;
        }

        /// <summary>
        ///     Commits the rows the mediation staged.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token supplied to the completion stage.</param>
        /// <returns>A task representing the asynchronous commit operation.</returns>
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     A result-producing command that stages its settlement order for the unit of work to commit.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the idempotency key.</param>
    /// <param name="Reference">The order reference, which carries a unique index.</param>
    /// <param name="FailHandler">Whether the handler throws instead of staging its row.</param>
    public sealed record ApplySettlementOrderCommand(Guid OrderId, string Reference, bool FailHandler) : ICommand<string>;

    /// <summary>
    ///     Stages the settlement order, recording each run on the shared counter.
    /// </summary>
    public sealed class ApplySettlementOrderCommandHandler : ICommandHandler<ApplySettlementOrderCommand, string>
    {
        /// <summary>
        ///     The scoped context the handler stages its row on.
        /// </summary>
        private readonly SettlementOrderDbContext _context;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly SettlementHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplySettlementOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler stages its row on.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public ApplySettlementOrderCommandHandler(SettlementOrderDbContext context, SettlementHandlerCounter counter)
        {
            _context = context;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt, which differs per run so a replay is distinguishable from a rerun.</returns>
        public Task<string> HandleAsync(ApplySettlementOrderCommand message, CancellationToken cancellationToken = default)
        {
            var run = _counter.NextRun();

            if (message.FailHandler)
            {
                throw new InvalidOperationException("The settlement handler fails for the settlement-order test.");
            }

            var receipt = $"receipt:{message.OrderId}:{run}";
            _context.Orders.Add(new SettlementOrderEntity
            {
                OrderId = message.OrderId,
                Reference = message.Reference,
                Receipt = receipt
            });

            return Task.FromResult(receipt);
        }
    }

    /// <summary>
    ///     Declares the settlement-order command idempotent with result replay.
    /// </summary>
    public sealed class ApplySettlementOrderCommandDefinition : IIdempotencyDefinition<ApplySettlementOrderCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<ApplySettlementOrderCommand>(command => command.OrderId.ToString("N"))
                with { Scope = "settlement-orders", ReplayResult = true };
    }

    /// <summary>
    ///     A result-producing command that saves its settlement order itself, without a unit of work.
    /// </summary>
    /// <param name="OrderId">The order identifier used as the idempotency key.</param>
    /// <param name="Reference">The order reference, which carries a unique index.</param>
    public sealed record ApplyDirectSettlementOrderCommand(Guid OrderId, string Reference) : ICommand<string>;

    /// <summary>
    ///     Saves the settlement order directly, recording each run on the shared counter.
    /// </summary>
    public sealed class ApplyDirectSettlementOrderCommandHandler : ICommandHandler<ApplyDirectSettlementOrderCommand, string>
    {
        /// <summary>
        ///     The scoped context the handler saves its row through.
        /// </summary>
        private readonly SettlementOrderDbContext _context;

        /// <summary>
        ///     Counts how many times the handler actually ran.
        /// </summary>
        private readonly SettlementHandlerCounter _counter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplyDirectSettlementOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler saves its row through.</param>
        /// <param name="counter">Counts how many times the handler actually ran.</param>
        public ApplyDirectSettlementOrderCommandHandler(SettlementOrderDbContext context, SettlementHandlerCounter counter)
        {
            _context = context;
            _counter = counter;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt, which differs per run so a replay is distinguishable from a rerun.</returns>
        public async Task<string> HandleAsync(ApplyDirectSettlementOrderCommand message, CancellationToken cancellationToken = default)
        {
            var run = _counter.NextRun();
            var receipt = $"receipt:{message.OrderId}:{run}";
            _context.Orders.Add(new SettlementOrderEntity
            {
                OrderId = message.OrderId,
                Reference = message.Reference,
                Receipt = receipt
            });

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return receipt;
        }
    }

    /// <summary>
    ///     Declares the direct settlement-order command idempotent with result replay.
    /// </summary>
    public sealed class ApplyDirectSettlementOrderCommandDefinition : IIdempotencyDefinition<ApplyDirectSettlementOrderCommand>
    {
        /// <summary>
        ///     Gets the idempotency declaration for the command.
        /// </summary>
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedBy<ApplyDirectSettlementOrderCommand>(command => command.OrderId.ToString("N"))
                with { Scope = "settlement-orders", ReplayResult = true };
    }

    /// <summary>
    ///     The stored state of one idempotency key observed during a settlement-order test.
    /// </summary>
    /// <param name="Key">The stored key.</param>
    /// <param name="IsCompleted">Whether the key was marked applied.</param>
    /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
    public sealed record StoredIdempotencyKey(string Key, bool IsCompleted, string? Payload);

    /// <summary>
    ///     An in-memory idempotency store without the separately-committed marker, so LiteBus settles it before the unit of work commits.
    /// </summary>
    /// <remarks>
    ///     Claims never expire: each test settles its keys before finishing, so no takeover path is needed.
    /// </remarks>
    public sealed class UnmarkedSettlementIdempotencyStore : IIdempotencyStore
    {
        /// <summary>
        ///     One key's remembered state.
        /// </summary>
        /// <param name="Fingerprint">The fingerprint the claiming delivery sent, or <see langword="null" /> when none.</param>
        /// <param name="IsCompleted">Whether the key was marked applied.</param>
        /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
        /// <param name="ClaimToken">The token fencing the current claim.</param>
        private sealed record ClaimEntry(string? Fingerprint, bool IsCompleted, string? Payload, string ClaimToken);

        /// <summary>
        ///     Guards the remembered keys, so a claim is checked and recorded atomically.
        /// </summary>
        private readonly object _entriesLock = new();

        /// <summary>
        ///     The remembered keys and their state.
        /// </summary>
        private readonly Dictionary<string, ClaimEntry> _entries = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the stored state of every remembered key.
        /// </summary>
        public IReadOnlyCollection<StoredIdempotencyKey> StoredKeys
        {
            get
            {
                lock (_entriesLock)
                {
                    return _entries
                        .Select(entry => new StoredIdempotencyKey(entry.Key, entry.Value.IsCompleted, entry.Value.Payload))
                        .ToList();
                }
            }
        }

        /// <inheritdoc />
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            lock (_entriesLock)
            {
                if (!_entries.TryGetValue(key, out var existing))
                {
                    var granted = Guid.NewGuid().ToString("N");
                    _entries[key] = new ClaimEntry(fingerprint, IsCompleted: false, Payload: null, ClaimToken: granted);

                    return Task.FromResult(IdempotencyClaim.Granted(granted));
                }

                if (existing.IsCompleted)
                {
                    if (existing.Fingerprint is not null
                        && fingerprint is not null
                        && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
                    }

                    return Task.FromResult(IdempotencyClaim.AlreadyCompleted(existing.Payload));
                }

                return Task.FromResult(IdempotencyClaim.InFlight);
            }
        }

        /// <inheritdoc />
        public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries[key] = existing with { IsCompleted = true, Payload = payload };
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && !existing.IsCompleted
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries.Remove(key);
                }
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An in-memory idempotency store that declares separately-committed writes and fails every settlement, so the test proves a post-commit settlement failure does not fail the mediation.
    /// </summary>
    /// <remarks>
    ///     Claims never expire: each test settles its keys before finishing, so no takeover path is needed.
    /// </remarks>
    public sealed class ThrowingSettlementIdempotencyStore : ISeparatelyCommittedIdempotencyStore
    {
        /// <summary>
        ///     One key's remembered state.
        /// </summary>
        /// <param name="Fingerprint">The fingerprint the claiming delivery sent, or <see langword="null" /> when none.</param>
        /// <param name="IsCompleted">Whether the key was marked applied.</param>
        /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
        /// <param name="ClaimToken">The token fencing the current claim.</param>
        private sealed record ClaimEntry(string? Fingerprint, bool IsCompleted, string? Payload, string ClaimToken);

        /// <summary>
        ///     Guards the remembered keys, so a claim is checked and recorded atomically.
        /// </summary>
        private readonly object _entriesLock = new();

        /// <summary>
        ///     The remembered keys and their state.
        /// </summary>
        private readonly Dictionary<string, ClaimEntry> _entries = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the stored state of every remembered key.
        /// </summary>
        public IReadOnlyCollection<StoredIdempotencyKey> StoredKeys
        {
            get
            {
                lock (_entriesLock)
                {
                    return _entries
                        .Select(entry => new StoredIdempotencyKey(entry.Key, entry.Value.IsCompleted, entry.Value.Payload))
                        .ToList();
                }
            }
        }

        /// <inheritdoc />
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            lock (_entriesLock)
            {
                if (!_entries.TryGetValue(key, out var existing))
                {
                    var granted = Guid.NewGuid().ToString("N");
                    _entries[key] = new ClaimEntry(fingerprint, IsCompleted: false, Payload: null, ClaimToken: granted);

                    return Task.FromResult(IdempotencyClaim.Granted(granted));
                }

                if (existing.IsCompleted)
                {
                    if (existing.Fingerprint is not null
                        && fingerprint is not null
                        && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    {
                        return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
                    }

                    return Task.FromResult(IdempotencyClaim.AlreadyCompleted(existing.Payload));
                }

                return Task.FromResult(IdempotencyClaim.InFlight);
            }
        }

        /// <inheritdoc />
        public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            throw new InvalidOperationException("Post-commit settlement fails for the settlement-order test.");
        }

        /// <inheritdoc />
        public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

            lock (_entriesLock)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && !existing.IsCompleted
                    && string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
                {
                    _entries.Remove(key);
                }
            }

            return Task.CompletedTask;
        }
    }
}
