using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;
using LiteBus.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.UnitOfWork;

/// <summary>
///     Verifies the unit-of-work boundary hook encloses self-saving command handlers in a real Entity Framework Core
///     transaction: success commits the rows the handler saved, a handler fault rolls them back, and a guard denial
///     opens no transaction.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreUnitOfWorkBoundaryTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture shared across tests.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreUnitOfWorkBoundaryTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreUnitOfWorkBoundaryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms a successful mediation commits the rows the handler saved through its own
    ///     <c>SaveChangesAsync</c> call.
    /// </summary>
    [SkippableFact]
    public async Task TrySendAsync_ShouldCommitHandlerSavedRows_OnSuccess()
    {
        var ordersTableName = await CreateOrdersTableAsync().ConfigureAwait(false);
        var recorder = new UnitOfWorkBoundaryRecorder();
        var provider = BuildProvider(ordersTableName, recorder);
        await using var configuredProvider = provider.ConfigureAwait(false);

        var orderId = Guid.NewGuid();
        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new SaveBoundaryOrderCommand(orderId, 42m, FailAfterSave: false)).ConfigureAwait(false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be($"receipt:{orderId}");
        recorder.Events.Should().Equal("begin", "commit");

        var stored = await LoadOrderAsync(ordersTableName, orderId).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Amount.Should().Be(42m);
    }

    /// <summary>
    ///     Confirms a handler that saves its row and then throws leaves no rows, because the mediation rolls the
    ///     boundary transaction back.
    /// </summary>
    [SkippableFact]
    public async Task TrySendAsync_ShouldLeaveNoRows_WhenTheHandlerThrowsAfterSaving()
    {
        var ordersTableName = await CreateOrdersTableAsync().ConfigureAwait(false);
        var recorder = new UnitOfWorkBoundaryRecorder();
        var provider = BuildProvider(ordersTableName, recorder);
        await using var configuredProvider = provider.ConfigureAwait(false);

        var orderId = Guid.NewGuid();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = () => mediator.TrySendAsync(new SaveBoundaryOrderCommand(orderId, 42m, FailAfterSave: true));

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        recorder.Events.Should().Equal("begin", "rollback");

        var storedCount = await CountOrdersAsync(ordersTableName, orderId).ConfigureAwait(false);
        storedCount.Should().Be(0);
    }

    /// <summary>
    ///     Confirms a guard denial opens no transaction: the handler never runs, nothing is recorded, and no rows
    ///     are stored.
    /// </summary>
    [SkippableFact]
    public async Task TrySendAsync_ShouldOpenNoTransaction_WhenAGuardDenies()
    {
        var ordersTableName = await CreateOrdersTableAsync().ConfigureAwait(false);
        var recorder = new UnitOfWorkBoundaryRecorder();
        var provider = BuildProvider(ordersTableName, recorder);
        await using var configuredProvider = provider.ConfigureAwait(false);

        var orderId = Guid.NewGuid();
        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new BlockedBoundaryOrderCommand(orderId)).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Denied);
        recorder.Events.Should().BeEmpty();

        var storedCount = await CountOrdersAsync(ordersTableName, orderId).ConfigureAwait(false);
        storedCount.Should().Be(0);
    }

    /// <summary>
    ///     Builds a provider that mediates the boundary commands with the Entity Framework Core boundary unit of work.
    /// </summary>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <param name="recorder">The recorder shared with the unit of work.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(string ordersTableName, UnitOfWorkBoundaryRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<BoundaryOrderDbContext>(_ =>
        {
            var builder = new DbContextOptionsBuilder<BoundaryOrderDbContext>()
                .UseNpgsql(CreateScopedConnectionString(ordersTableName));

            return new BoundaryOrderDbContext(builder.Options, ordersTableName);
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<SaveBoundaryOrderCommand>();
                commands.Register<SaveBoundaryOrderCommandHandler>();
                commands.Register<BlockedBoundaryOrderCommand>();
                commands.Register<BlockedBoundaryOrderCommandHandler>();
                commands.Register<BlockedBoundaryOrderGuard>();
                commands.UseUnitOfWork<EfCoreBoundaryUnitOfWork>();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Creates the domain orders table for one test run.
    /// </summary>
    /// <returns>The domain orders table name.</returns>
    private async Task<string> CreateOrdersTableAsync()
    {
        var ordersTableName = $"orders_ef_uow_{Guid.NewGuid():N}";

        var context = CreateContext(ordersTableName);
        await using (context.ConfigureAwait(false))
        {
            var quotedSchema = PostgreSqlIdentifier.Quote(EfCoreProviderTestInfrastructure.SchemaName);
            var qualifiedOrdersTable = PostgreSqlIdentifier.Qualify(EfCoreProviderTestInfrastructure.SchemaName, ordersTableName);

#pragma warning disable EF1002 // PostgreSqlIdentifier validates and quotes the generated schema and table identifiers.
            await context.Database.ExecuteSqlRawAsync(
                $"""
                 CREATE SCHEMA IF NOT EXISTS {quotedSchema};
                 CREATE TABLE IF NOT EXISTS {qualifiedOrdersTable} (
                     order_id uuid NOT NULL PRIMARY KEY,
                     amount numeric NOT NULL);
                 """).ConfigureAwait(false);
#pragma warning restore EF1002

            return ordersTableName;
        }
    }

    /// <summary>
    ///     Creates a database context against the domain orders table for one test run.
    /// </summary>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <returns>The configured context.</returns>
    private BoundaryOrderDbContext CreateContext(string ordersTableName)
    {
        var builder = new DbContextOptionsBuilder<BoundaryOrderDbContext>()
            .UseNpgsql(CreateScopedConnectionString(ordersTableName));

        return new BoundaryOrderDbContext(builder.Options, ordersTableName);
    }

    /// <summary>
    ///     Builds a connection string scoped to one orders table so Entity Framework Core model caching stays isolated
    ///     per test table.
    /// </summary>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <returns>The scoped connection string.</returns>
    private string CreateScopedConnectionString(string ordersTableName)
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            ApplicationName = $"litebus_ef_uow_{ordersTableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Loads one boundary order outside any mediation.
    /// </summary>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The stored row, or <see langword="null" /> when no row carries the identifier.</returns>
    private async Task<BoundaryOrderEntity?> LoadOrderAsync(string ordersTableName, Guid orderId)
    {
        var context = CreateContext(ordersTableName);
        await using (context.ConfigureAwait(false))
        {
            return await context.Orders.SingleOrDefaultAsync(order => order.OrderId == orderId).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts the stored rows for one boundary order outside any mediation.
    /// </summary>
    /// <param name="ordersTableName">The domain orders table name.</param>
    /// <param name="orderId">The order identifier.</param>
    /// <returns>The number of rows carrying the identifier.</returns>
    private async Task<int> CountOrdersAsync(string ordersTableName, Guid orderId)
    {
        var context = CreateContext(ordersTableName);
        await using (context.ConfigureAwait(false))
        {
            return await context.Orders.CountAsync(order => order.OrderId == orderId).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Collects the boundary, commit, and rollback calls observed during one unit-of-work boundary test run.
    /// </summary>
    public sealed class UnitOfWorkBoundaryRecorder
    {
        /// <summary>
        ///     Gets the observed events, in the order they happened.
        /// </summary>
        public ConcurrentQueue<string> Events { get; } = new();
    }

    /// <summary>
    ///     A minimal domain row used to prove the boundary transaction commits and rolls back handler-saved rows.
    /// </summary>
    public sealed class BoundaryOrderEntity
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
    ///     The Entity Framework Core context the boundary tests stage and verify rows through.
    /// </summary>
    public sealed class BoundaryOrderDbContext : DbContext
    {
        /// <summary>
        ///     The domain orders table name.
        /// </summary>
        private readonly string _ordersTableName;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BoundaryOrderDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="ordersTableName">The domain orders table name.</param>
        public BoundaryOrderDbContext(DbContextOptions<BoundaryOrderDbContext> options, string ordersTableName)
            : base(options)
        {
            _ordersTableName = ordersTableName;
        }

        /// <summary>
        ///     Gets the domain orders tracked by the context.
        /// </summary>
        public DbSet<BoundaryOrderEntity> Orders => Set<BoundaryOrderEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BoundaryOrderEntity>(entity =>
            {
                entity.ToTable(_ordersTableName, EfCoreProviderTestInfrastructure.SchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.Amount).HasColumnName("amount").HasColumnType("numeric");
            });
        }
    }

    /// <summary>
    ///     Opens a real Entity Framework Core transaction in <c>BeginAsync</c> and commits or rolls it back as the
    ///     mediation ends.
    /// </summary>
    public sealed class EfCoreBoundaryUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
    {
        /// <summary>
        ///     The scoped context the handler saved its rows through.
        /// </summary>
        private readonly BoundaryOrderDbContext _context;

        /// <summary>
        ///     The recorder shared with the test.
        /// </summary>
        private readonly UnitOfWorkBoundaryRecorder _recorder;

        /// <summary>
        ///     The transaction opened by <c>BeginAsync</c>, or <see langword="null" /> when no mediation holds one.
        /// </summary>
        private IDbContextTransaction? _transaction;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EfCoreBoundaryUnitOfWork" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler saved its rows through.</param>
        /// <param name="recorder">The recorder shared with the test.</param>
        public EfCoreBoundaryUnitOfWork(BoundaryOrderDbContext context, UnitOfWorkBoundaryRecorder recorder)
        {
            _context = context;
            _recorder = recorder;
        }

        /// <inheritdoc />
        public async Task BeginAsync(CancellationToken cancellationToken = default)
        {
            _transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _recorder.Events.Enqueue("begin");
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }

            _recorder.Events.Enqueue("commit");
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction is not null)
            {
                await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }

            _recorder.Events.Enqueue("rollback");
        }
    }

    /// <summary>
    ///     A command whose handler saves its row itself and then optionally throws.
    /// </summary>
    /// <param name="OrderId">The order identifier.</param>
    /// <param name="Amount">The order amount.</param>
    /// <param name="FailAfterSave">Whether the handler throws after saving its row.</param>
    public sealed record SaveBoundaryOrderCommand(Guid OrderId, decimal Amount, bool FailAfterSave) : ICommand<string>;

    /// <summary>
    ///     Saves the boundary order through its own <c>SaveChangesAsync</c> call, then throws when the command asks
    ///     for it.
    /// </summary>
    public sealed class SaveBoundaryOrderCommandHandler : ICommandHandler<SaveBoundaryOrderCommand, string>
    {
        /// <summary>
        ///     The scoped context the handler saves its row through.
        /// </summary>
        private readonly BoundaryOrderDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SaveBoundaryOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler saves its row through.</param>
        public SaveBoundaryOrderCommandHandler(BoundaryOrderDbContext context)
        {
            _context = context;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public async Task<string> HandleAsync(SaveBoundaryOrderCommand message, CancellationToken cancellationToken = default)
        {
            _context.Orders.Add(new BoundaryOrderEntity { OrderId = message.OrderId, Amount = message.Amount });
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (message.FailAfterSave)
            {
                throw new InvalidOperationException("The boundary handler fails after saving for the unit-of-work boundary test.");
            }

            return $"receipt:{message.OrderId}";
        }
    }

    /// <summary>
    ///     A command the test guard always denies.
    /// </summary>
    /// <param name="OrderId">The order identifier.</param>
    public sealed record BlockedBoundaryOrderCommand(Guid OrderId) : ICommand<string>;

    /// <summary>
    ///     Would save the boundary order, but never runs because the guard denies first.
    /// </summary>
    public sealed class BlockedBoundaryOrderCommandHandler : ICommandHandler<BlockedBoundaryOrderCommand, string>
    {
        /// <summary>
        ///     The scoped context the handler would save its row through.
        /// </summary>
        private readonly BoundaryOrderDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BlockedBoundaryOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler would save its row through.</param>
        public BlockedBoundaryOrderCommandHandler(BoundaryOrderDbContext context)
        {
            _context = context;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The settlement receipt.</returns>
        public async Task<string> HandleAsync(BlockedBoundaryOrderCommand message, CancellationToken cancellationToken = default)
        {
            _context.Orders.Add(new BoundaryOrderEntity { OrderId = message.OrderId, Amount = 1m });
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return $"receipt:{message.OrderId}";
        }
    }

    /// <summary>
    ///     Always denies the blocked boundary order, so the test proves a denial opens no transaction.
    /// </summary>
    public sealed class BlockedBoundaryOrderGuard : ICommandGuard<BlockedBoundaryOrderCommand>
    {
        /// <summary>
        ///     Decides whether the message is permitted to proceed.
        /// </summary>
        /// <param name="message">The message being mediated.</param>
        /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
        /// <returns>The verdict that denies the message.</returns>
        public Task<Verdict> DecideAsync(BlockedBoundaryOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Deny($"Blocked boundary order {message.OrderId} for the unit-of-work boundary test."));
        }
    }
}
