using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends command batches through Entity Framework Core: one batch commits its domain and outbox rows together,
///     and a refused batch leaves nothing behind.
/// </summary>
internal static class CommandBatchSmoke
{
    /// <summary>
    ///     The contract name under which the batch order event is registered.
    /// </summary>
    private const string OrderSubmittedContract = "smoke.batches.events.order-submitted";

    /// <summary>
    ///     Runs the command batch scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<SmokeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<ApplySmokeBatchOrderCommand>();
                commands.Register<ApplySmokeBatchOrderCommandHandler>();
                commands.Register<ApplySmokeBatchOrderGuard>();
                commands.UseUnitOfWork<SmokeDbContextUnitOfWork>();
            });
            liteBus.AddOutbox(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(ef =>
                {
                    ef.UseDbContext<SmokeDbContext>();
                    ef.EnableSaveChangesInterceptor();
                });
                outbox.Contracts.Register<SmokeBatchOrderSubmittedEvent>(OrderSubmittedContract);
            });
        });
        services.AddDbContext<SmokeDbContext>((serviceProvider, options) => options
            .UseNpgsql(connectionString)
            .AddLiteBusOutboxInterceptor(serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

        await using var provider = services.BuildServiceProvider();
        var batches = provider.GetRequiredService<ICommandBatchMediator>();

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        IReadOnlyList<ICommand> committed =
        [
            new ApplySmokeBatchOrderCommand(firstId, 10m, Blocked: false),
            new ApplySmokeBatchOrderCommand(secondId, 20m, Blocked: false),
            new ApplySmokeBatchOrderCommand(thirdId, 30m, Blocked: false)
        ];

        var committedResult = await batches.TrySendManyAsync(committed).ConfigureAwait(false);
        Smoke.Expect(committedResult.Outcome == CommandBatchOutcome.Committed, "a batch of three orders commits");
        Smoke.Expect(committedResult.FailedIndex is null, "a committed batch names no failed command");

        Guid[] committedIds = [firstId, secondId, thirdId];
        Smoke.Expect(
            await CountOrdersAsync(connectionString, committedIds).ConfigureAwait(false) == 3,
            "a committed batch stores three domain rows");
        Smoke.Expect(
            await CountOutboxRowsAsync(connectionString, committedIds).ConfigureAwait(false) == 3,
            "a committed batch stores three outbox rows");

        var refusedFirstId = Guid.NewGuid();
        var refusedSecondId = Guid.NewGuid();
        var refusedThirdId = Guid.NewGuid();

        IReadOnlyList<ICommand> refused =
        [
            new ApplySmokeBatchOrderCommand(refusedFirstId, 10m, Blocked: false),
            new ApplySmokeBatchOrderCommand(refusedSecondId, 20m, Blocked: true),
            new ApplySmokeBatchOrderCommand(refusedThirdId, 30m, Blocked: false)
        ];

        var refusedResult = await batches.TrySendManyAsync(refused).ConfigureAwait(false);
        Smoke.Expect(refusedResult.Outcome == CommandBatchOutcome.RolledBack, "a batch with a denied command rolls back");
        Smoke.Expect(refusedResult.FailedIndex == 1, "the rolled back batch names the denied command");
        Smoke.Expect(
            refusedResult.Entries.Count == 3 && refusedResult.Entries[2].Executed == false,
            "the commands after the denied command do not run");

        Guid[] refusedIds = [refusedFirstId, refusedSecondId, refusedThirdId];
        Smoke.Expect(
            await CountOrdersAsync(connectionString, refusedIds).ConfigureAwait(false) == 0,
            "a refused batch leaves no domain rows");
        Smoke.Expect(
            await CountOutboxRowsAsync(connectionString, refusedIds).ConfigureAwait(false) == 0,
            "a refused batch leaves no outbox rows");
    }

    /// <summary>
    ///     Counts the domain rows stored for the given order identifiers.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="orderIds">The order identifiers to count.</param>
    /// <returns>The number of stored domain rows.</returns>
    private static async Task<int> CountOrdersAsync(string connectionString, Guid[] orderIds)
    {
        await using (var context = SmokeDbContext.Create(connectionString))
        {
            return await context.SmokeBatchOrders.CountAsync(order => orderIds.Contains(order.OrderId)).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts the outbox rows whose payload carries one of the given order identifiers.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="orderIds">The order identifiers to look for.</param>
    /// <returns>The number of stored outbox rows.</returns>
    private static async Task<int> CountOutboxRowsAsync(string connectionString, Guid[] orderIds)
    {
        var markers = orderIds.Select(static id => id.ToString()).ToArray();

        await using (var context = SmokeDbContext.Create(connectionString))
        {
            var payloads = await context.OutboxMessages.Select(static message => message.Payload).ToListAsync().ConfigureAwait(false);
            return payloads.Count(payload => markers.Any(payload.Contains));
        }
    }
}

/// <summary>
///     A command that stages its order row and its outbox event for the batch unit of work.
/// </summary>
/// <param name="OrderId">The order identifier written to the domain row and the outbox event.</param>
/// <param name="Total">The order total written to the domain row.</param>
/// <param name="Blocked">Whether the guard denies the command.</param>
internal sealed record ApplySmokeBatchOrderCommand(Guid OrderId, decimal Total, bool Blocked) : ICommand;

/// <summary>
///     The integration event the batch order handler stages into the transactional outbox.
/// </summary>
internal sealed record SmokeBatchOrderSubmittedEvent
{
    /// <summary>
    ///     Gets the order identifier the event carries.
    /// </summary>
    public Guid OrderId { get; init; }
}

/// <summary>
///     Stages the order row and its outbox event on the scoped smoke context.
/// </summary>
internal sealed class ApplySmokeBatchOrderCommandHandler : ICommandHandler<ApplySmokeBatchOrderCommand>
{
    /// <summary>
    ///     The scoped context the handler stages its row on.
    /// </summary>
    private readonly SmokeDbContext _context;

    /// <summary>
    ///     The transactional outbox the handler stages its event through.
    /// </summary>
    private readonly ITransactionalOutbox<SmokeDbContext> _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ApplySmokeBatchOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="context">The scoped context the handler stages its row on.</param>
    /// <param name="outbox">The transactional outbox the handler stages its event through.</param>
    public ApplySmokeBatchOrderCommandHandler(SmokeDbContext context, ITransactionalOutbox<SmokeDbContext> outbox)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outbox);
        _context = context;
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ApplySmokeBatchOrderCommand message, CancellationToken cancellationToken = default)
    {
        _context.SmokeBatchOrders.Add(new SmokeBatchOrder { OrderId = message.OrderId, Total = message.Total });

        await _outbox.EnqueueAsync(
            OutboxEnqueueItem<SmokeBatchOrderSubmittedEvent>.From(new SmokeBatchOrderSubmittedEvent { OrderId = message.OrderId }),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     Denies the batch order command when the message marks it blocked.
/// </summary>
internal sealed class ApplySmokeBatchOrderGuard : ICommandGuard<ApplySmokeBatchOrderCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ApplySmokeBatchOrderCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(message.Blocked
            ? Verdict.Deny("The smoke order is blocked for the command batch scenario.")
            : Verdict.Allow);
    }
}
