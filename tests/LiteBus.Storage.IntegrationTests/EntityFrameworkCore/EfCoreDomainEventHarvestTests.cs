using System.ComponentModel.DataAnnotations.Schema;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Verifies domain events buffered on tracked entities harvest into the outbox before collected events stage.
/// </summary>
public sealed class EfCoreDomainEventHarvestTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The contract name staged events are registered under.
    /// </summary>
    private const string ContractName = "tests.events.order-placed";

    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreDomainEventHarvestTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreDomainEventHarvestTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies an added entity that implements the domain event contract stages its events and clears them.
    /// </summary>
    [Fact]
    public async Task Added_entity_implementing_the_contract_stages_and_clears()
    {
        await ResetTablesAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.DefaultContract)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<OrderEntity>();
        ((OrderEntity) probe.HarvestedAggregates[0]).DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an added entity with configured accessors stages its events and clears them.
    /// </summary>
    [Fact]
    public async Task Added_entity_with_configured_accessors_stages_and_clears()
    {
        await ResetTablesAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.ConfiguredAccessor)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
        probe.HarvestedAggregates.Should().HaveCount(1);
        probe.HarvestedAggregates[0].Should().BeOfType<CustomOrderEntity>();
        ((CustomOrderEntity) probe.HarvestedAggregates[0]).GetPendingEvents().Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an unchanged entity with buffered events stages them, because the harvester reads every tracked entry.
    /// </summary>
    [Fact]
    public async Task Unchanged_entity_with_buffered_events_stages()
    {
        await ResetTablesAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await SeedOrderAsync(provider, orderId).ConfigureAwait(false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.Unchanged)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies a deleted entity stages its final events before its row is removed.
    /// </summary>
    [Fact]
    public async Task Deleted_entity_stages_its_final_events()
    {
        await ResetTablesAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await SeedOrderAsync(provider, orderId).ConfigureAwait(false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.Deleted)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);

        var verification = CreateContext();
        await using (verification.ConfigureAwait(false))
        {
            var stored = await verification.Orders
                .SingleOrDefaultAsync(order => order.Id == orderId).ConfigureAwait(false);

            stored.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies a modified entity stages the events it raised.
    /// </summary>
    [Fact]
    public async Task Modified_entity_stages_its_events()
    {
        await ResetTablesAsync().ConfigureAwait(false);
        var probe = new HarvestProbe();
        var provider = BuildProvider(probe);
        await using var configuredProvider = provider.ConfigureAwait(false);
        var orderId = Guid.NewGuid();

        await SeedOrderAsync(provider, orderId).ConfigureAwait(false);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SubmitOrderCommand(orderId, HarvestScenario.Modified)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = ContractName },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(1);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
    }

    /// <summary>
    ///     Builds one provider with Entity Framework Core domain event harvesting enabled.
    /// </summary>
    /// <param name="probe">The probe that captures harvested aggregates.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(HarvestProbe probe)
    {
        var services = new ServiceCollection();
        var storeOptions = CreateStoreOptions();
        services.AddSingleton(probe);
        services.AddSingleton(storeOptions);
        services.AddDbContext<HarvestOutboxDbContext>((provider, options) => ConfigureOptions(provider, options));
        services.AddDbContextFactory<HarvestOutboxDbContext>((provider, options) => ConfigureOptions(provider, options));

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<SubmitOrderCommand>();
                commands.Register<SubmitOrderCommandHandler>();
                commands.Register<EfCoreSaveChangesObserver>();
            });

            registry.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<OrderPlacedHarvestEvent>(ContractName);
                outbox.HarvestDomainEvents<CustomOrderEntity>(
                    entity => entity.GetPendingEvents(),
                    entity => entity.ClearPendingEvents());

                outbox.UseEntityFrameworkCoreStorage(storage =>
                {
                    storage.UseDbContext<HarvestOutboxDbContext>();
                    storage.UseOptions(storeOptions);
                    storage.EnableSaveChangesInterceptor();
                    storage.HarvestDomainEvents();
                });

                outbox.StageCollectedEvents();
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Configures one database context with the fixture connection and the outbox save-changes interceptor.
    /// </summary>
    /// <param name="provider">The service provider that resolves the interceptor.</param>
    /// <param name="options">The options builder used to configure a database context.</param>
    private void ConfigureOptions(IServiceProvider provider, DbContextOptionsBuilder options)
    {
        options.UseSqlite(_fixture.ConnectionString);
        options.AddLiteBusOutboxInterceptor(provider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
    }

    /// <summary>
    ///     Creates the outbox store options for harvest tests.
    /// </summary>
    /// <returns>The outbox store options.</returns>
    private static EntityFrameworkCoreOutboxStoreOptions CreateStoreOptions()
    {
        return new EntityFrameworkCoreOutboxStoreOptions
        {
            SchemaName = "main",
            TableName = "outbox_ef_harvest_tests",
            LeaseProvider = EfCoreStorageProvider.Sqlite
        };
    }

    /// <summary>
    ///     Creates a database context against the fixture database.
    /// </summary>
    /// <returns>The configured database context.</returns>
    private HarvestOutboxDbContext CreateContext()
    {
        return new HarvestOutboxDbContext(
            new DbContextOptionsBuilder<HarvestOutboxDbContext>().UseSqlite(_fixture.ConnectionString).Options,
            CreateStoreOptions());
    }

    /// <summary>
    ///     Ensures the harvest tables exist and removes rows before one test run.
    /// </summary>
    private async Task ResetTablesAsync()
    {
        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            await context.Orders.ExecuteDeleteAsync().ConfigureAwait(false);
            await context.CustomOrders.ExecuteDeleteAsync().ConfigureAwait(false);
            await context.OutboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Stores one order without events, so a test can load it in a tracked state.
    /// </summary>
    /// <param name="provider">The service provider that creates independent contexts.</param>
    /// <param name="orderId">The order identifier to seed.</param>
    private static async Task SeedOrderAsync(ServiceProvider provider, Guid orderId)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<HarvestOutboxDbContext>>();
        var seed = factory.CreateDbContext();
        await using (seed.ConfigureAwait(false))
        {
            seed.Orders.Add(new OrderEntity { Id = orderId, Total = 10m });
            await seed.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Selects which entity operation the harvest command handler performs.
    /// </summary>
    public enum HarvestScenario
    {
        /// <summary>
        ///     Adds a new entity that implements the domain event contract.
        /// </summary>
        DefaultContract,

        /// <summary>
        ///     Adds a new entity read through configured accessors.
        /// </summary>
        ConfiguredAccessor,

        /// <summary>
        ///     Loads a stored entity and raises an event without touching a mapped property.
        /// </summary>
        Unchanged,

        /// <summary>
        ///     Loads a stored entity, raises an event, and deletes it.
        /// </summary>
        Deleted,

        /// <summary>
        ///     Loads a stored entity, changes it, and raises an event.
        /// </summary>
        Modified
    }

    /// <summary>
    ///     Adds or mutates order entities for one harvest scenario.
    /// </summary>
    /// <param name="OrderId">The order identifier carried by the command.</param>
    /// <param name="Scenario">The entity operation the handler performs.</param>
    public sealed record SubmitOrderCommand(Guid OrderId, HarvestScenario Scenario) : ICommand<string>;

    /// <summary>
    ///     Adds or mutates order entities for the scenario the command names.
    /// </summary>
    public sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, string>
    {
        /// <summary>
        ///     The database context the handler stages entities on.
        /// </summary>
        private readonly HarvestOutboxDbContext _context;

        /// <summary>
        ///     The probe that captures harvested aggregates.
        /// </summary>
        private readonly HarvestProbe _probe;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SubmitOrderCommandHandler" /> class.
        /// </summary>
        /// <param name="context">The database context the handler stages entities on.</param>
        /// <param name="probe">The probe that captures harvested aggregates.</param>
        public SubmitOrderCommandHandler(HarvestOutboxDbContext context, HarvestProbe probe)
        {
            _context = context;
            _probe = probe;
        }

        /// <summary>
        ///     Handles the command by adding or mutating entities for its scenario.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A receipt naming the stored order.</returns>
        public async Task<string> HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
        {
            switch (message.Scenario)
            {
                case HarvestScenario.DefaultContract:
                    {
                        var aggregate = new OrderEntity { Id = message.OrderId, Total = 42m };
                        aggregate.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _context.Orders.Add(aggregate);
                        _probe.HarvestedAggregates.Add(aggregate);
                        break;
                    }

                case HarvestScenario.ConfiguredAccessor:
                    {
                        var aggregate = new CustomOrderEntity { Id = message.OrderId, Total = 42m };
                        aggregate.AddPendingEvent(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _context.CustomOrders.Add(aggregate);
                        _probe.HarvestedAggregates.Add(aggregate);
                        break;
                    }

                case HarvestScenario.Unchanged:
                    {
                        var stored = await _context.Orders
                            .SingleAsync(order => order.Id == message.OrderId, cancellationToken).ConfigureAwait(false);
                        stored.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _probe.HarvestedAggregates.Add(stored);
                        break;
                    }

                case HarvestScenario.Deleted:
                    {
                        var stored = await _context.Orders
                            .SingleAsync(order => order.Id == message.OrderId, cancellationToken).ConfigureAwait(false);
                        stored.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _context.Remove(stored);
                        _probe.HarvestedAggregates.Add(stored);
                        break;
                    }

                case HarvestScenario.Modified:
                    {
                        var stored = await _context.Orders
                            .SingleAsync(order => order.Id == message.OrderId, cancellationToken).ConfigureAwait(false);
                        stored.Total = 84m;
                        stored.Raise(new OrderPlacedHarvestEvent { OrderId = message.OrderId });
                        _probe.HarvestedAggregates.Add(stored);
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown harvest scenario '{message.Scenario}'.");
            }

            return $"stored:{message.OrderId}";
        }
    }

    /// <summary>
    ///     Saves the scoped database context after staging, so staged rows commit with the entities.
    /// </summary>
    [HandlerPriority(HandlerPriorities.UnitOfWork)]
    public sealed class EfCoreSaveChangesObserver : ICommandCompletionHandler<SubmitOrderCommand>
    {
        /// <summary>
        ///     The database context saved when the mediation succeeds.
        /// </summary>
        private readonly HarvestOutboxDbContext _context;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EfCoreSaveChangesObserver" /> class.
        /// </summary>
        /// <param name="context">The database context saved when the mediation succeeds.</param>
        public EfCoreSaveChangesObserver(HarvestOutboxDbContext context)
        {
            _context = context;
        }

        /// <summary>
        ///     Saves the context when the mediation completed successfully.
        /// </summary>
        /// <param name="context">The typed completion context observed at the end of mediation.</param>
        /// <param name="cancellationToken">The cancellation token for the completion stage.</param>
        /// <returns>A task representing the asynchronous completion-handling operation.</returns>
        public async Task HandleCompletionAsync(
            MessageCompletionContext<SubmitOrderCommand> context,
            CancellationToken cancellationToken)
        {
            if (context.Outcome != MediationOutcome.Succeeded)
            {
                return;
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Captures the aggregates the command handler staged, so tests can verify harvesting cleared them.
    /// </summary>
    public sealed class HarvestProbe
    {
        /// <summary>
        ///     Gets the aggregates the command handler staged.
        /// </summary>
        public List<object> HarvestedAggregates { get; } = [];
    }

    /// <summary>
    ///     Outbox database context with order aggregates for domain event harvest tests.
    /// </summary>
    public sealed class HarvestOutboxDbContext : DbContext, IOutboxDbContext
    {
        /// <summary>
        ///     The store options that control schema mapping.
        /// </summary>
        private readonly EntityFrameworkCoreOutboxStoreOptions _storeOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HarvestOutboxDbContext" /> class.
        /// </summary>
        /// <param name="options">The context options.</param>
        /// <param name="storeOptions">The outbox store options.</param>
        public HarvestOutboxDbContext(
            DbContextOptions<HarvestOutboxDbContext> options,
            EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options)
        {
            _storeOptions = storeOptions;
        }

        /// <summary>
        ///     Gets the outbox messages tracked by this context.
        /// </summary>
        public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

        /// <summary>
        ///     Gets the order entities tracked by this context.
        /// </summary>
        public DbSet<OrderEntity> Orders => Set<OrderEntity>();

        /// <summary>
        ///     Gets the custom order entities tracked by this context.
        /// </summary>
        public DbSet<CustomOrderEntity> CustomOrders => Set<CustomOrderEntity>();

        /// <summary>
        ///     Configures the outbox message mapping for the SQLite test store.
        /// </summary>
        /// <param name="modelBuilder">The model builder used to configure entity mappings.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.GetModelBuilderConfiguration(_storeOptions, EfCoreStorageProvider.Sqlite);
        }
    }

    /// <summary>
    ///     An order entity that buffers its domain events through the shared contract.
    /// </summary>
    public sealed class OrderEntity : IHasDomainEvents
    {
        /// <summary>
        ///     The buffered domain events raised since the last harvest.
        /// </summary>
        private readonly List<object> _events = [];

        /// <summary>
        ///     Gets or sets the entity identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        ///     Gets the domain events the entity raised and has not yet handed over.
        /// </summary>
        [NotMapped]
        public IReadOnlyCollection<object> DomainEvents => _events;

        /// <summary>
        ///     Buffers one domain event for the next harvest.
        /// </summary>
        /// <param name="domainEvent">The domain event to buffer.</param>
        public void Raise(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearDomainEvents()
        {
            _events.Clear();
        }
    }

    /// <summary>
    ///     An order entity that buffers its domain events in its own list, read through configured accessors.
    /// </summary>
    public sealed class CustomOrderEntity
    {
        /// <summary>
        ///     The buffered domain events raised since the last harvest.
        /// </summary>
        private readonly List<object> _pendingEvents = [];

        /// <summary>
        ///     Gets or sets the entity identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets or sets the order total.
        /// </summary>
        public decimal Total { get; set; }

        /// <summary>
        ///     Reads the buffered domain events raised since the last harvest.
        /// </summary>
        /// <returns>The buffered domain events.</returns>
        public IEnumerable<object> GetPendingEvents()
        {
            return _pendingEvents;
        }

        /// <summary>
        ///     Buffers one domain event for the next harvest.
        /// </summary>
        /// <param name="domainEvent">The domain event to buffer.</param>
        public void AddPendingEvent(object domainEvent)
        {
            _pendingEvents.Add(domainEvent);
        }

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearPendingEvents()
        {
            _pendingEvents.Clear();
        }
    }

    /// <summary>
    ///     The domain event harvested from order entities.
    /// </summary>
    public sealed record OrderPlacedHarvestEvent
    {
        /// <summary>
        ///     Gets the placed order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }
    }
}
