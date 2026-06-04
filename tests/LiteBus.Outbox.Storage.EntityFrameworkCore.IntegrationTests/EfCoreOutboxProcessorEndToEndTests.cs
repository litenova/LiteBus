using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     End-to-end outbox processor tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
public sealed class EfCoreOutboxProcessorEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxProcessorEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxProcessorEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that <see cref="IOutboxProcessor.ProcessPendingAsync" /> publishes a stored event through EF Core storage.
    /// </summary>
    /// <returns>A task that completes when the event is published.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore()
    {
        var storeOptions = new EfCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = $"outbox_ef_e2e_{Guid.NewGuid():N}"
        };

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(_fixture.ConnectionString);
        await LiteBus.Outbox.Storage.PostgreSql.PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new LiteBus.Outbox.Storage.PostgreSql.PostgreSqlOutboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            });

        var recorder = new EventRecorder();

        await using var provider = BuildProvider(_fixture.ConnectionString, storeOptions, recorder);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions
        {
            Id = messageId
        });

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        EfCoreOutboxStoreOptions storeOptions,
        EventRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<ProcessorEndToEndOutboxDbContext>(_ =>
        {
            var builder = new DbContextOptionsBuilder<ProcessorEndToEndOutboxDbContext>()
                .UseNpgsql(connectionString);
            return new ProcessorEndToEndOutboxDbContext(builder.Options, storeOptions);
        });

        services.AddLiteBus(modules =>
        {
            modules.AddEfCoreOutboxStorage(builder =>
            {
                builder.UseDbContext<ProcessorEndToEndOutboxDbContext>();
                builder.UseOptions(storeOptions);
            });

            modules.AddEventModule(module =>
            {
                module.Register<OrderSubmittedEventHandler>();
            });

            modules.AddOutboxModule(outbox =>
            {
                outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "efcore-outbox-e2e",
                    Retry = new RetryOptions { UseJitter = false }
                });
            });

            modules.AddOutboxInProcessDispatcher();
        });

        return services.BuildServiceProvider();
    }

    internal sealed record OrderSubmittedIntegrationEvent
    {
        public required Guid OrderId { get; init; }
    }

    internal sealed class OrderSubmittedEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
    {
        private readonly EventRecorder _recorder;

        public OrderSubmittedEventHandler(EventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    internal sealed class EventRecorder
    {
        private readonly List<OrderSubmittedIntegrationEvent> _events = [];

        public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

        public void Record(OrderSubmittedIntegrationEvent @event) => _events.Add(@event);
    }

    internal sealed class ProcessorEndToEndOutboxDbContext : DbContext, IOutboxDbContext
    {
        private readonly EfCoreOutboxStoreOptions _storeOptions;

        public ProcessorEndToEndOutboxDbContext(DbContextOptions<ProcessorEndToEndOutboxDbContext> options, EfCoreOutboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.PostgreSql);
        }
    }
}
