using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     End-to-end inbox processor tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
public sealed class EfCoreInboxProcessorEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxProcessorEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxProcessorEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that <see cref="IInboxProcessor.ProcessPendingAsync" /> executes a scheduled command through EF Core storage.
    /// </summary>
    /// <returns>A task that completes when the command is handled.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldExecuteScheduledCommandThroughEfCoreStore()
    {
        var storeOptions = new EfCoreInboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"inbox_ef_e2e_{Guid.NewGuid():N}"
        };

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(_fixture.ConnectionString);
        await LiteBus.Inbox.Storage.PostgreSql.PostgreSqlInboxSchema.EnsureAsync(
            dataSource,
            new LiteBus.Inbox.Storage.PostgreSql.PostgreSqlInboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            });

        var recorder = new CommandRecorder();

        await using var provider = BuildProvider(_fixture.ConnectionString, storeOptions, recorder);
        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        EfCoreInboxStoreOptions storeOptions,
        CommandRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<ProcessorEndToEndInboxDbContext>(_ =>
        {
            var builder = new DbContextOptionsBuilder<ProcessorEndToEndInboxDbContext>()
                .UseNpgsql(connectionString);
            return new ProcessorEndToEndInboxDbContext(builder.Options, storeOptions);
        });

        services.AddLiteBus(modules =>
        {
            modules.AddEfCoreInboxStorage(builder =>
            {
                builder.UseDbContext<ProcessorEndToEndInboxDbContext>();
                builder.UseOptions(storeOptions);
            });

            modules.AddCommandModule(module =>
            {
                module.Register<ShipOrderCommand>();
                module.Register<ShipOrderCommandHandler>();
            });

            modules.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "efcore-inbox-e2e",
                    Retry = new RetryOptions { UseJitter = false }
                });
            });

            modules.AddInboxInProcessDispatcher();
        });

        return services.BuildServiceProvider();
    }

    internal sealed record ShipOrderCommand : ICommand
    {
        public required Guid OrderId { get; init; }

        public string? IdempotencyKey { get; init; }
    }

    internal sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
    {
        private readonly CommandRecorder _recorder;

        public ShipOrderCommandHandler(CommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class CommandRecorder
    {
        private readonly List<ShipOrderCommand> _commands = [];

        public IReadOnlyList<ShipOrderCommand> Commands => _commands;

        public void Record(ShipOrderCommand command) => _commands.Add(command);
    }

    internal sealed class ProcessorEndToEndInboxDbContext : DbContext, IInboxDbContext
    {
        private readonly EfCoreInboxStoreOptions _storeOptions;

        public ProcessorEndToEndInboxDbContext(DbContextOptions<ProcessorEndToEndInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
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
