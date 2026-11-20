using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends one command whose aggregate raises a staged domain event through the EF Core outbox, then reads the staged row back.
/// </summary>
internal static class DomainEventStageSmoke
{
    /// <summary>
    ///     The contract name under which the staged domain event is registered.
    /// </summary>
    private const string OrderPlacedContract = "smoke.domain-events.order-placed";

    /// <summary>
    ///     Runs the staged domain event scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        await using (var setup = DomainEventStageDbContext.Create(connectionString))
        {
            await setup.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"DomainEventStageOrders\" (\"order_id\" uuid NOT NULL PRIMARY KEY, \"total\" numeric NOT NULL)").ConfigureAwait(false);
        }

        var services = new ServiceCollection();
        services.AddDbContext<DomainEventStageDbContext>((provider, options) => ConfigureOptions(provider, options, connectionString));
        services.AddDbContextFactory<DomainEventStageDbContext>((provider, options) => ConfigureOptions(provider, options, connectionString));
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<PlaceDomainEventStageOrderCommand>();
                commands.Register<PlaceDomainEventStageOrderCommandHandler>();
                commands.Register<DomainEventStageSaveChangesObserver>();
            });
            liteBus.AddOutbox(outbox =>
            {
                outbox.Contracts.Register<DomainEventStageOrderPlaced>(OrderPlacedContract);
                outbox.UseEntityFrameworkCoreStorage(storage =>
                {
                    storage.UseDbContext<DomainEventStageDbContext>();
                    storage.EnableSaveChangesInterceptor();
                    storage.HarvestDomainEvents();
                });
                outbox.StageCollectedEvents();
                outbox.StageDomainEvent<DomainEventStageOrderPlaced>();
                outbox.RequireDomainEventDecision();
            });
        });

        await using var provider = services.BuildServiceProvider();
        var orderId = Guid.NewGuid();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PlaceDomainEventStageOrderCommand(orderId)).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = OrderPlacedContract },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        var marker = orderId.ToString();
        var staged = page.Items.Where(envelope => envelope.Payload.Contains(marker, StringComparison.Ordinal)).ToList();
        Smoke.Expect(staged.Count == 1, "the staged domain event writes one outbox row");
        Smoke.Expect(staged[0].Status == OutboxStatus.Pending, "the staged row is pending");

        var payload = JsonSerializer.Deserialize<DomainEventStageOrderPlaced>(
            staged[0].Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Smoke.Expect(payload is not null && payload.OrderId == orderId, "the staged row carries the event unchanged");

        await using (var verification = DomainEventStageDbContext.Create(connectionString))
        {
            var stored = await verification.Orders
                .SingleOrDefaultAsync(order => order.OrderId == orderId).ConfigureAwait(false);
            Smoke.Expect(stored is not null, "the aggregate row commits with the staged row");
        }

        var model = provider.GetRequiredService<ICompositionModel>();
        var section = model.Sections.OfType<OutboxDomainEventCompositionSection>().SingleOrDefault();
        Smoke.Expect(section is not null, "the composition model reports the domain event decisions");
        Smoke.Expect(
            section!.Decisions.Any(decision =>
                decision.EventType == typeof(DomainEventStageOrderPlaced) &&
                decision.Kind == DomainEventDecisionKind.Stage),
            "the composition model shows the Stage kind");
        Smoke.Expect(
            section!.UndecidedPolicy == UndecidedDomainEventPolicy.Throw,
            "the composition model shows the required decision policy");
    }

    /// <summary>
    ///     Configures one scenario database context with the smoke connection and the outbox save-changes interceptor.
    /// </summary>
    /// <param name="provider">The service provider that resolves the interceptor.</param>
    /// <param name="options">The options builder used to configure a database context.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static void ConfigureOptions(IServiceProvider provider, DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString);
        options.AddLiteBusOutboxInterceptor(provider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
    }
}

/// <summary>
///     A command that stores one order aggregate which raises the staged domain event.
/// </summary>
/// <param name="OrderId">The order identifier written to the aggregate and its event.</param>
internal sealed record PlaceDomainEventStageOrderCommand(Guid OrderId) : ICommand;

/// <summary>
///     The domain event the scenario aggregate raises and the outbox stages unchanged.
/// </summary>
internal sealed record DomainEventStageOrderPlaced
{
    /// <summary>
    ///     Gets the placed order identifier.
    /// </summary>
    public required Guid OrderId { get; init; }
}

/// <summary>
///     Stores one order aggregate and raises the staged domain event on it.
/// </summary>
internal sealed class PlaceDomainEventStageOrderCommandHandler : ICommandHandler<PlaceDomainEventStageOrderCommand>
{
    /// <summary>
    ///     The database context the handler stages the aggregate on.
    /// </summary>
    private readonly DomainEventStageDbContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlaceDomainEventStageOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="context">The database context the handler stages the aggregate on.</param>
    public PlaceDomainEventStageOrderCommandHandler(DomainEventStageDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task HandleAsync(PlaceDomainEventStageOrderCommand message, CancellationToken cancellationToken = default)
    {
        var aggregate = new DomainEventStageOrder { OrderId = message.OrderId, Total = 42m };
        aggregate.Raise(new DomainEventStageOrderPlaced { OrderId = message.OrderId });
        _context.Orders.Add(aggregate);
        return Task.CompletedTask;
    }
}

/// <summary>
///     Saves the scoped scenario database context after staging, so the staged row commits with the aggregate.
/// </summary>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class DomainEventStageSaveChangesObserver : ICommandCompletionHandler<PlaceDomainEventStageOrderCommand>
{
    /// <summary>
    ///     The database context saved when the mediation succeeds.
    /// </summary>
    private readonly DomainEventStageDbContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventStageSaveChangesObserver" /> class.
    /// </summary>
    /// <param name="context">The database context saved when the mediation succeeds.</param>
    public DomainEventStageSaveChangesObserver(DomainEventStageDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task HandleCompletionAsync(
        MessageCompletionContext<PlaceDomainEventStageOrderCommand> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     An order aggregate that buffers its domain events through the shared contract.
/// </summary>
internal sealed class DomainEventStageOrder : IHasDomainEvents
{
    /// <summary>
    ///     The buffered domain events raised since the last harvest.
    /// </summary>
    private readonly List<object> _events = [];

    /// <summary>
    ///     Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    ///     Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    ///     Gets the domain events the aggregate raised and has not yet handed over.
    /// </summary>
    [NotMapped]
    public IReadOnlyCollection<object> DomainEvents => _events;

    /// <summary>
    ///     Buffers one domain event for the next harvest.
    /// </summary>
    /// <param name="domainEvent">The domain event to buffer.</param>
    public void Raise(object domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
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
///     The scenario database context, holding the shared outbox table and the scenario aggregate table.
/// </summary>
internal sealed class DomainEventStageDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventStageDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public DomainEventStageDbContext(DbContextOptions<DomainEventStageDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    ///     Gets the outbox messages.
    /// </summary>
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <summary>
    ///     Gets the scenario order aggregates.
    /// </summary>
    public DbSet<DomainEventStageOrder> Orders => Set<DomainEventStageOrder>();

    /// <summary>
    ///     Creates a context on the smoke database.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A new context.</returns>
    internal static DomainEventStageDbContext Create(string connectionString)
    {
        return new DomainEventStageDbContext(new DbContextOptionsBuilder<DomainEventStageDbContext>().UseNpgsql(connectionString).Options);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(
            new EntityFrameworkCoreOutboxStoreOptions(),
            EfCoreStorageProvider.PostgreSql);

        modelBuilder.Entity<DomainEventStageOrder>(entity =>
        {
            entity.ToTable("DomainEventStageOrders");
            entity.HasKey(order => order.OrderId);
            entity.Property(order => order.OrderId).HasColumnName("order_id");
            entity.Property(order => order.Total).HasColumnName("total").HasColumnType("numeric");
        });
    }
}
