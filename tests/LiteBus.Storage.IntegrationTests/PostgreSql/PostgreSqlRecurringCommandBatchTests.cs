using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Commands.Recurring.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the recurring command batch loop against PostgreSQL: one committed batch per pass, leased passes running on one host at a time, and refusal rollback with error backoff.
/// </summary>
public sealed class PostgreSqlRecurringCommandBatchTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringCommandBatchTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlRecurringCommandBatchTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task BatchBackgroundService_WhenSourceReturnsCommands_ShouldCommitOneBatchPerPass()
    {
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);

        var passLog = new BatchPassLog();
        var handlerLog = new BatchHandlerLog();
        var script = new CommittingBatchScript(passLog);

        var provider = BuildCommittingProvider(script, passLog, handlerLog);
        await using var configuredProvider = provider.ConfigureAwait(false);

        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            provider,
            typeof(RecurringCommandBatchBackgroundService<CommittingBatchSource>));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

        try
        {
            // The gate is closed, so the first pass produces an empty batch, which sends nothing.
            await script.WaitForPassesAsync(1).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(300), runCts.Token).ConfigureAwait(false);
            handlerLog.TotalRuns.Should().Be(0);

            script.Release();
            await handlerLog.WaitForTotalRunsAsync(CommittingBatchScript.TotalCommands).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }

        handlerLog.TotalRuns.Should().Be(CommittingBatchScript.TotalCommands);

        var produced = script.ProducedPasses;
        produced.Should().HaveCount(CommittingBatchScript.CommandPasses);

        var rows = await ReadOrdersAsync(produced.SelectMany(pass => pass).ToList()).ConfigureAwait(false);
        rows.Should().HaveCount(CommittingBatchScript.TotalCommands);

        for (var pass = 0; pass < CommittingBatchScript.CommandPasses; pass++)
        {
            var passNumber = pass;
            rows.Count(row => row.PassNumber == passNumber).Should().Be(CommittingBatchScript.CommandsPerPass);

            foreach (var orderId in produced[pass])
            {
                rows.Should().ContainSingle(row => row.OrderId == orderId);
            }
        }
    }

    [SkippableFact]
    public async Task BatchBackgroundService_TwoHostsWithRequireLease_ShouldNeverRunTheSamePass()
    {
        var leaseOptions = CreateLeaseOptions();
        await PostgreSqlRecurringLeaseSchema.EnsureAsync(_fixture.DataSource, leaseOptions).ConfigureAwait(false);
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);

        var script = new LeasedBatchScript();
        var handlerLog = new BatchHandlerLog();

        var providerA = BuildLeasedProvider("host-a", leaseOptions, script, handlerLog);
        await using var configuredProviderA = providerA.ConfigureAwait(false);

        var providerB = BuildLeasedProvider("host-b", leaseOptions, script, handlerLog);
        await using var configuredProviderB = providerB.ConfigureAwait(false);

        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            providerA,
            typeof(RecurringCommandBatchBackgroundService<LeasedBatchSource>));
        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            providerB,
            typeof(RecurringCommandBatchBackgroundService<LeasedBatchSource>));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerA, runCts.Token).ConfigureAwait(false);
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(providerB, runCts.Token).ConfigureAwait(false);

        try
        {
            await script.WaitForHostPassesAsync("host-a", 3).ConfigureAwait(false);
            await script.WaitForHostPassesAsync("host-b", 3).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerB, CancellationToken.None).ConfigureAwait(false);
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(providerA, CancellationToken.None).ConfigureAwait(false);
        }

        script.MaxInFlight.Should().Be(1);

        var produced = script.ProducedOrderIds;
        produced.Count.Should().BeGreaterThanOrEqualTo(6);

        var rows = await ReadOrdersAsync(produced).ConfigureAwait(false);
        var committed = rows.Select(row => row.OrderId).ToList();

        // At most the pass in flight at shutdown produced commands without committing them.
        committed.Count.Should().BeGreaterThanOrEqualTo(4);
        committed.Count.Should().BeGreaterThanOrEqualTo(produced.Count - 1);
        committed.Distinct().Count().Should().Be(committed.Count);

        foreach (var orderId in committed)
        {
            produced.Should().Contain(orderId);
        }
    }

    [SkippableFact]
    public async Task BatchBackgroundService_WhenCommandIsRefused_ShouldRollBackAndWaitErrorBackoff()
    {
        await EnsureDomainSchemaAsync().ConfigureAwait(false);
        await EnsureDomainTableAsync().ConfigureAwait(false);

        var passLog = new BatchPassLog();
        var handlerLog = new BatchHandlerLog();
        var script = new RefusedBatchScript(passLog);
        var logger = new RecordingLogger<RecurringCommandBatchBackgroundService<RefusedBatchSource>>();

        var provider = BuildRefusedProvider(script, passLog, handlerLog, logger);
        await using var configuredProvider = provider.ConfigureAwait(false);

        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            provider,
            typeof(RecurringCommandBatchBackgroundService<RefusedBatchSource>));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

        try
        {
            // The interval is an hour, so a second pass is only possible after the error backoff.
            await script.WaitForPassesAsync(2).ConfigureAwait(false);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
        }

        var startedAt = script.PassStartedAt;
        startedAt.Count.Should().BeGreaterThanOrEqualTo(2);
        (startedAt[1] - startedAt[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50));

        var warnings = logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.EventId.Id == 2107)
            .ToList();
        warnings.Should().NotBeEmpty();

        warnings[0].Message.Should().Contain(nameof(RefusedBatchSource));
        warnings[0].Message.Should().Contain("index 1");
        warnings[0].Message.Should().Contain(nameof(RefusedBatchOrderCommand));
        warnings[0].Message.Should().Contain(RefuseBatchOrderGuard.RefusalCode);

        handlerLog.RunsFor<ApplyBatchOrderCommand>().Should().BeGreaterThanOrEqualTo(1);
        handlerLog.RunsFor<RefusedBatchOrderCommand>().Should().Be(0);

        var produced = script.ProducedOrderIds;
        produced.Should().NotBeEmpty();

        var rows = await ReadOrdersAsync(produced).ConfigureAwait(false);
        rows.Should().BeEmpty();
    }

    private static PostgreSqlRecurringLeaseOptions CreateLeaseOptions()
    {
        return new PostgreSqlRecurringLeaseOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"recurring_batch_leases_{Guid.NewGuid():N}"
        };
    }

    private ServiceProvider BuildCommittingProvider(
        CommittingBatchScript script,
        BatchPassLog passLog,
        BatchHandlerLog handlerLog)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BatchOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(script);
        services.AddSingleton(passLog);
        services.AddSingleton(handlerLog);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<ApplyBatchOrderCommand>();
                builder.Register<ApplyBatchOrderCommandHandler>();
                builder.UseUnitOfWork<BatchOrderUnitOfWork>();
                builder.AddRecurringBatch<CommittingBatchSource>(host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(50);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(50);
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildLeasedProvider(
        string hostName,
        PostgreSqlRecurringLeaseOptions leaseOptions,
        LeasedBatchScript script,
        BatchHandlerLog handlerLog)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BatchOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(script);
        services.AddSingleton(handlerLog);
        services.AddSingleton(new BatchHostIdentity(hostName));
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<ApplyBatchOrderCommand>();
                builder.Register<ApplyBatchOrderCommandHandler>();
                builder.UseUnitOfWork<BatchOrderUnitOfWork>();
                builder.UsePostgreSqlRecurringLease(pg =>
                {
                    pg.UseDataSource(_fixture.DataSource);
                    pg.UseOptions(leaseOptions);
                });
                builder.AddRecurringBatch<LeasedBatchSource>(host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(50);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(50);
                    host.RequireLease = true;
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildRefusedProvider(
        RefusedBatchScript script,
        BatchPassLog passLog,
        BatchHandlerLog handlerLog,
        RecordingLogger<RecurringCommandBatchBackgroundService<RefusedBatchSource>> logger)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BatchOrderDbContext>(db => db.UseNpgsql(_fixture.ConnectionString));
        services.AddSingleton(script);
        services.AddSingleton(passLog);
        services.AddSingleton(handlerLog);
        services.AddSingleton<ILogger<RecurringCommandBatchBackgroundService<RefusedBatchSource>>>(logger);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder =>
            {
                builder.Register<ApplyBatchOrderCommand>();
                builder.Register<ApplyBatchOrderCommandHandler>();
                builder.Register<RefusedBatchOrderCommand>();
                builder.Register<RefusedBatchOrderCommandHandler>();
                builder.Register<RefuseBatchOrderGuard>();
                builder.UseUnitOfWork<BatchOrderUnitOfWork>();
                builder.AddRecurringBatch<RefusedBatchSource>(host =>
                {
                    host.Interval = TimeSpan.FromHours(1);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(100);
                });
            });
        });

        return services.BuildServiceProvider();
    }

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

    private async Task EnsureDomainTableAsync()
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    private BatchOrderDbContext CreateDomainContext()
    {
        var builder = new DbContextOptionsBuilder<BatchOrderDbContext>().UseNpgsql(_fixture.ConnectionString);

        return new BatchOrderDbContext(builder.Options);
    }

    private async Task<IReadOnlyList<BatchOrderEntity>> ReadOrdersAsync(IReadOnlyList<Guid> orderIds)
    {
        var context = CreateDomainContext();
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.Orders
            .Where(order => orderIds.Contains(order.OrderId))
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     One batch order row written by the test handlers.
    /// </summary>
    public sealed class BatchOrderEntity
    {
        /// <summary>
        ///     Gets or sets the order identifier.
        /// </summary>
        public Guid OrderId { get; set; }

        /// <summary>
        ///     Gets or sets the source pass that produced the order.
        /// </summary>
        public int PassNumber { get; set; }
    }

    /// <summary>
    ///     The Entity Framework Core context the batch tests stage and verify rows through.
    /// </summary>
    public sealed class BatchOrderDbContext : DbContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchOrderDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        public BatchOrderDbContext(DbContextOptions<BatchOrderDbContext> options)
            : base(options)
        {
        }

        /// <summary>
        ///     Gets the batch orders tracked by the context.
        /// </summary>
        public DbSet<BatchOrderEntity> Orders => Set<BatchOrderEntity>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BatchOrderEntity>(entity =>
            {
                entity.ToTable("recurring_batch_orders", PostgreSqlTestInfrastructure.TestSchemaName);
                entity.HasKey(order => order.OrderId);
                entity.Property(order => order.OrderId).HasColumnName("order_id");
                entity.Property(order => order.PassNumber).HasColumnName("pass_number");
            });
        }
    }

    /// <summary>
    ///     Commits the scoped batch order context as the mediation unit of work.
    /// </summary>
    public sealed class BatchOrderUnitOfWork : IUnitOfWork
    {
        /// <summary>
        ///     The scoped context the handler staged its row on.
        /// </summary>
        private readonly BatchOrderDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchOrderUnitOfWork" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler staged its row on.</param>
        public BatchOrderUnitOfWork(BatchOrderDbContext context)
        {
            _context = context;
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     An order command the recurring batch loop commits.
    /// </summary>
    /// <param name="OrderId">The order identifier.</param>
    /// <param name="PassNumber">The source pass that produced the command.</param>
    public sealed record ApplyBatchOrderCommand(Guid OrderId, int PassNumber) : ICommand;

    /// <summary>
    ///     Stages the batch order for the unit of work to commit, recording each run on the shared log.
    /// </summary>
    public sealed class ApplyBatchOrderCommandHandler : ICommandHandler<ApplyBatchOrderCommand>
    {
        /// <summary>
        ///     The scoped context the handler stages its row on.
        /// </summary>
        private readonly BatchOrderDbContext _context;

        /// <summary>
        ///     Records how many times the handler actually ran.
        /// </summary>
        private readonly BatchHandlerLog _log;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ApplyBatchOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler stages its row on.</param>
        /// <param name="log">Records how many times the handler actually ran.</param>
        public ApplyBatchOrderCommandHandler(BatchOrderDbContext context, BatchHandlerLog log)
        {
            _context = context;
            _log = log;
        }

        /// <inheritdoc />
        public Task HandleAsync(ApplyBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(message.GetType());
            _context.Orders.Add(new BatchOrderEntity
            {
                OrderId = message.OrderId,
                PassNumber = message.PassNumber
            });

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An order command the test guard refuses on every pass.
    /// </summary>
    /// <param name="OrderId">The order identifier.</param>
    /// <param name="PassNumber">The source pass that produced the command.</param>
    public sealed record RefusedBatchOrderCommand(Guid OrderId, int PassNumber) : ICommand;

    /// <summary>
    ///     Stages the refused batch order. It never runs while the guard denies the command.
    /// </summary>
    public sealed class RefusedBatchOrderCommandHandler : ICommandHandler<RefusedBatchOrderCommand>
    {
        /// <summary>
        ///     The scoped context the handler stages its row on.
        /// </summary>
        private readonly BatchOrderDbContext _context;

        /// <summary>
        ///     Records how many times the handler actually ran.
        /// </summary>
        private readonly BatchHandlerLog _log;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RefusedBatchOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The scoped context the handler stages its row on.</param>
        /// <param name="log">Records how many times the handler actually ran.</param>
        public RefusedBatchOrderCommandHandler(BatchOrderDbContext context, BatchHandlerLog log)
        {
            _context = context;
            _log = log;
        }

        /// <inheritdoc />
        public Task HandleAsync(RefusedBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(message.GetType());
            _context.Orders.Add(new BatchOrderEntity
            {
                OrderId = message.OrderId,
                PassNumber = message.PassNumber
            });

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Refuses the refused batch order on every pass with a fixed refusal code.
    /// </summary>
    public sealed class RefuseBatchOrderGuard : ICommandGuard<RefusedBatchOrderCommand>
    {
        /// <summary>
        ///     The machine-readable code the guard reports on every refusal.
        /// </summary>
        public const string RefusalCode = "BATCH_TEST_REFUSAL";

        /// <inheritdoc />
        public Task<Verdict> DecideAsync(RefusedBatchOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Deny("The recurring batch test refuses this command on every pass.", RefusalCode));
        }
    }

    /// <summary>
    ///     Counts handler runs and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class BatchHandlerLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a run is recorded so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     Guards the recorded runs, so a run is recorded atomically across hosts.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        ///     The runtime type of every handled command, in record order.
        /// </summary>
        private readonly List<Type> _runs = [];

        /// <inheritdoc />
        public void Dispose()
        {
            _signal.Dispose();
        }

        /// <summary>
        ///     Gets the number of handler runs recorded so far.
        /// </summary>
        public int TotalRuns
        {
            get
            {
                lock (_sync)
                {
                    return _runs.Count;
                }
            }
        }

        /// <summary>
        ///     Counts the recorded runs of one command type.
        /// </summary>
        /// <typeparam name="TCommand">The command type to count.</typeparam>
        /// <returns>The number of recorded runs for the command type.</returns>
        public int RunsFor<TCommand>()
            where TCommand : ICommand
        {
            lock (_sync)
            {
                return _runs.Count(run => run == typeof(TCommand));
            }
        }

        /// <summary>
        ///     Records one handler run.
        /// </summary>
        /// <param name="commandType">The runtime type of the handled command.</param>
        public void Record(Type commandType)
        {
            lock (_sync)
            {
                _runs.Add(commandType);
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> handler runs have been recorded.
        /// </summary>
        /// <param name="target">The number of runs to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForTotalRunsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                while (TotalRuns < target)
                {
                    await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {target} handler runs; observed {TotalRuns}.",
                    exception);
            }
        }
    }

    /// <summary>
    ///     Counts source passes and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class BatchPassLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a pass is recorded so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     Guards the recorded pass start times.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        ///     The start time of every recorded pass, in record order.
        /// </summary>
        private readonly List<DateTimeOffset> _startedAt = [];

        /// <inheritdoc />
        public void Dispose()
        {
            _signal.Dispose();
        }

        /// <summary>
        ///     Gets the number of passes recorded so far.
        /// </summary>
        public int Passes
        {
            get
            {
                lock (_sync)
                {
                    return _startedAt.Count;
                }
            }
        }

        /// <summary>
        ///     Gets the start time of every recorded pass, in record order.
        /// </summary>
        public IReadOnlyList<DateTimeOffset> PassStartedAt
        {
            get
            {
                lock (_sync)
                {
                    return [.. _startedAt];
                }
            }
        }

        /// <summary>
        ///     Records one source pass.
        /// </summary>
        public void RecordPass()
        {
            lock (_sync)
            {
                _startedAt.Add(DateTimeOffset.UtcNow);
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> passes have been recorded.
        /// </summary>
        /// <param name="target">The number of passes to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForPassesAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                while (Passes < target)
                {
                    await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {target} source passes; observed {Passes}.",
                    exception);
            }
        }
    }

    /// <summary>
    ///     Names the host a leased batch source runs on, so one shared script tells two hosts apart.
    /// </summary>
    public sealed class BatchHostIdentity
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BatchHostIdentity" /> class.
        /// </summary>
        /// <param name="name">The host name recorded with every pass.</param>
        public BatchHostIdentity(string name)
        {
            Name = name;
        }

        /// <summary>
        ///     Gets the host name recorded with every pass.
        /// </summary>
        public string Name { get; }
    }

    /// <summary>
    ///     Produces a fixed number of command passes behind a release gate, and empty batches otherwise.
    /// </summary>
    public sealed class CommittingBatchScript
    {
        /// <summary>
        ///     The number of passes that return commands after the gate opens.
        /// </summary>
        public const int CommandPasses = 3;

        /// <summary>
        ///     The number of commands each command pass returns.
        /// </summary>
        public const int CommandsPerPass = 3;

        /// <summary>
        ///     The total number of commands the script produces.
        /// </summary>
        public const int TotalCommands = CommandPasses * CommandsPerPass;

        /// <summary>
        ///     Counts the source passes.
        /// </summary>
        private readonly BatchPassLog _passes;

        /// <summary>
        ///     Guards the gate and the produced passes.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        ///     The order identifiers produced by each command pass, in pass order.
        /// </summary>
        private readonly List<IReadOnlyList<Guid>> _produced = [];

        /// <summary>
        ///     Whether the gate is open and passes may return commands.
        /// </summary>
        private bool _released;

        /// <summary>
        ///     The number of command passes returned so far.
        /// </summary>
        private int _commandPassesReturned;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommittingBatchScript" /> class.
        /// </summary>
        /// <param name="passes">Counts the source passes.</param>
        public CommittingBatchScript(BatchPassLog passes)
        {
            _passes = passes;
        }

        /// <summary>
        ///     Gets the order identifiers produced by each command pass, in pass order.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<Guid>> ProducedPasses
        {
            get
            {
                lock (_sync)
                {
                    return [.. _produced];
                }
            }
        }

        /// <summary>
        ///     Opens the gate so passes return commands.
        /// </summary>
        public void Release()
        {
            lock (_sync)
            {
                _released = true;
            }
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> passes have been recorded.
        /// </summary>
        /// <param name="target">The number of passes to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public Task WaitForPassesAsync(int target)
        {
            return _passes.WaitForPassesAsync(target);
        }

        /// <summary>
        ///     Creates the batch sent on one pass of the loop.
        /// </summary>
        /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
        /// <returns>
        ///     The commands the pass sends as one batch, or <see cref="RecurringCommandBatch.Empty" /> when the gate is
        ///     closed or the fixed passes are spent.
        /// </returns>
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            _passes.RecordPass();

            lock (_sync)
            {
                if (!_released || _commandPassesReturned >= CommandPasses)
                {
                    return Task.FromResult(RecurringCommandBatch.Empty);
                }

                var passNumber = _commandPassesReturned;
                _commandPassesReturned++;

                var identifiers = new List<Guid>(CommandsPerPass);

                for (var index = 0; index < CommandsPerPass; index++)
                {
                    identifiers.Add(Guid.NewGuid());
                }

                _produced.Add([.. identifiers]);

                IReadOnlyList<ICommand> commands = [.. identifiers.Select(identifier => new ApplyBatchOrderCommand(identifier, passNumber))];

                return Task.FromResult(RecurringCommandBatch.From(commands));
            }
        }
    }

    /// <summary>
    ///     The batch source the one-batch-per-pass test resolves from a fresh scope on every pass.
    /// </summary>
    public sealed class CommittingBatchSource : IRecurringCommandBatchSource
    {
        /// <summary>
        ///     Produces the scripted batches.
        /// </summary>
        private readonly CommittingBatchScript _script;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommittingBatchSource" /> class.
        /// </summary>
        /// <param name="script">Produces the scripted batches.</param>
        public CommittingBatchSource(CommittingBatchScript script)
        {
            _script = script;
        }

        /// <inheritdoc />
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return _script.CreateBatchAsync(cancellationToken);
        }
    }

    /// <summary>
    ///     Produces one command per pass from two hosts while tracking how many passes overlap.
    /// </summary>
    public sealed class LeasedBatchScript : IDisposable
    {
        /// <summary>
        ///     How long each pass holds the lease before producing its command, so a second host running the same pass would overlap here.
        /// </summary>
        private static readonly TimeSpan SourceHoldDelay = TimeSpan.FromMilliseconds(250);

        /// <summary>
        ///     Signals each time a pass produces its command so a waiter can re-check the counts.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     Guards the per-host pass counts and the produced identifiers.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        ///     The passes recorded per host name.
        /// </summary>
        private readonly Dictionary<string, int> _passesByHost = new(StringComparer.Ordinal);

        /// <summary>
        ///     The order identifiers produced so far, in produce order.
        /// </summary>
        private readonly List<Guid> _producedOrderIds = [];

        /// <summary>
        ///     The number of passes currently inside the source.
        /// </summary>
        private int _inFlight;

        /// <summary>
        ///     The pass number assigned to the most recently produced command.
        /// </summary>
        private int _passSequence;

        /// <inheritdoc />
        public void Dispose()
        {
            _signal.Dispose();
        }

        /// <summary>
        ///     Gets the highest number of passes observed inside the source at once.
        /// </summary>
        public int MaxInFlight { get; private set; }

        /// <summary>
        ///     Gets the order identifiers produced so far, in produce order.
        /// </summary>
        public IReadOnlyList<Guid> ProducedOrderIds
        {
            get
            {
                lock (_sync)
                {
                    return [.. _producedOrderIds];
                }
            }
        }

        /// <summary>
        ///     Counts the passes recorded for one host.
        /// </summary>
        /// <param name="hostName">The host name to count.</param>
        /// <returns>The number of recorded passes for the host.</returns>
        public int PassesFor(string hostName)
        {
            lock (_sync)
            {
                return _passesByHost.TryGetValue(hostName, out var count) ? count : 0;
            }
        }

        /// <summary>
        ///     Waits until one host has recorded at least <paramref name="target" /> passes.
        /// </summary>
        /// <param name="hostName">The host name to wait for.</param>
        /// <param name="target">The number of passes to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForHostPassesAsync(string hostName, int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                while (PassesFor(hostName) < target)
                {
                    await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for {target} passes on '{hostName}'; observed {PassesFor(hostName)}.",
                    exception);
            }
        }

        /// <summary>
        ///     Holds the lease briefly, then creates the one-command batch sent on this pass.
        /// </summary>
        /// <param name="hostName">The host name recorded with the pass.</param>
        /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
        /// <returns>The one-command batch the pass sends.</returns>
        public async Task<RecurringCommandBatch> CreateBatchAsync(string hostName, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref _inFlight);

            lock (_sync)
            {
                if (inFlight > MaxInFlight)
                {
                    MaxInFlight = inFlight;
                }
            }

            try
            {
                await Task.Delay(SourceHoldDelay, cancellationToken).ConfigureAwait(false);

                var passNumber = Interlocked.Increment(ref _passSequence);
                var orderId = Guid.NewGuid();

                lock (_sync)
                {
                    _producedOrderIds.Add(orderId);
                    _passesByHost[hostName] = _passesByHost.TryGetValue(hostName, out var count) ? count + 1 : 1;
                }

                _signal.Release();

                return RecurringCommandBatch.From([new ApplyBatchOrderCommand(orderId, passNumber)]);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    /// <summary>
    ///     The batch source both leased hosts resolve, so they contend on one lease name.
    /// </summary>
    public sealed class LeasedBatchSource : IRecurringCommandBatchSource
    {
        /// <summary>
        ///     Produces the scripted batches and tracks overlap across hosts.
        /// </summary>
        private readonly LeasedBatchScript _script;

        /// <summary>
        ///     Names the host this source runs on.
        /// </summary>
        private readonly BatchHostIdentity _identity;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LeasedBatchSource" /> class.
        /// </summary>
        /// <param name="script">Produces the scripted batches and tracks overlap across hosts.</param>
        /// <param name="identity">Names the host this source runs on.</param>
        public LeasedBatchSource(LeasedBatchScript script, BatchHostIdentity identity)
        {
            _script = script;
            _identity = identity;
        }

        /// <inheritdoc />
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return _script.CreateBatchAsync(_identity.Name, cancellationToken);
        }
    }

    /// <summary>
    ///     Produces a batch whose second command the guard refuses, on every pass.
    /// </summary>
    public sealed class RefusedBatchScript
    {
        /// <summary>
        ///     Counts the source passes.
        /// </summary>
        private readonly BatchPassLog _passes;

        /// <summary>
        ///     Guards the produced identifiers.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        ///     The order identifiers produced so far, in produce order.
        /// </summary>
        private readonly List<Guid> _producedOrderIds = [];

        /// <summary>
        ///     The pass number assigned to the most recently produced batch.
        /// </summary>
        private int _passSequence;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RefusedBatchScript" /> class.
        /// </summary>
        /// <param name="passes">Counts the source passes.</param>
        public RefusedBatchScript(BatchPassLog passes)
        {
            _passes = passes;
        }

        /// <summary>
        ///     Gets the order identifiers produced so far, in produce order.
        /// </summary>
        public IReadOnlyList<Guid> ProducedOrderIds
        {
            get
            {
                lock (_sync)
                {
                    return [.. _producedOrderIds];
                }
            }
        }

        /// <summary>
        ///     Gets the start time of every recorded pass, in record order.
        /// </summary>
        public IReadOnlyList<DateTimeOffset> PassStartedAt => _passes.PassStartedAt;

        /// <summary>
        ///     Waits until at least <paramref name="target" /> passes have been recorded.
        /// </summary>
        /// <param name="target">The number of passes to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public Task WaitForPassesAsync(int target)
        {
            return _passes.WaitForPassesAsync(target);
        }

        /// <summary>
        ///     Creates the batch sent on one pass of the loop.
        /// </summary>
        /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
        /// <returns>One accepted command followed by one refused command.</returns>
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            _passes.RecordPass();

            var passNumber = Interlocked.Increment(ref _passSequence);
            var acceptedId = Guid.NewGuid();
            var refusedId = Guid.NewGuid();

            lock (_sync)
            {
                _producedOrderIds.Add(acceptedId);
                _producedOrderIds.Add(refusedId);
            }

            IReadOnlyList<ICommand> commands =
            [
                new ApplyBatchOrderCommand(acceptedId, passNumber),
                new RefusedBatchOrderCommand(refusedId, passNumber)
            ];

            return Task.FromResult(RecurringCommandBatch.From(commands));
        }
    }

    /// <summary>
    ///     The batch source the refusal test resolves from a fresh scope on every pass.
    /// </summary>
    public sealed class RefusedBatchSource : IRecurringCommandBatchSource
    {
        /// <summary>
        ///     Produces the scripted batches.
        /// </summary>
        private readonly RefusedBatchScript _script;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RefusedBatchSource" /> class.
        /// </summary>
        /// <param name="script">Produces the scripted batches.</param>
        public RefusedBatchSource(RefusedBatchScript script)
        {
            _script = script;
        }

        /// <inheritdoc />
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return _script.CreateBatchAsync(cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
            }
        }

        internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message);
    }
}
