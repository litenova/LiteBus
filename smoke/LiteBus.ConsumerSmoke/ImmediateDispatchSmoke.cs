using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Drives the EF Core outbox with per-handler delivery, subscriber names, and immediate dispatch: the immediate
///     handler's effect is visible when <see cref="ICommandMediator.SendAsync(ICommand, CommandMediationSettings, CancellationToken)" />
///     returns, and a failing immediate handler is delivered later by a processor pass.
/// </summary>
internal static class ImmediateDispatchSmoke
{
    /// <summary>
    ///     The contract name under which the placed-order event is registered.
    /// </summary>
    private const string OrderPlacedContract = "smoke.immediate.events.order-placed";

    /// <summary>
    ///     The contract name under which the flaky placed-order event is registered.
    /// </summary>
    private const string FlakyOrderPlacedContract = "smoke.immediate.events.flaky-order-placed";

    /// <summary>
    ///     The subscriber name recorded on the projection handler's envelope.
    /// </summary>
    internal const string ProjectionSubscriberName = "smoke.order-projection";

    /// <summary>
    ///     The subscriber name recorded on the flaky projection handler's envelope.
    /// </summary>
    internal const string FlakyProjectionSubscriberName = "smoke.flaky-projection";

    /// <summary>
    ///     Runs the immediate dispatch scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var recorder = new SmokeRecorder();
        var gate = new SmokeFlakyDeliveryGate();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(gate);
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<PlaceSmokeOrderCommand>();
                commands.Register<PlaceSmokeOrderCommandHandler>();
                commands.Register<PlaceSmokeFlakyOrderCommand>();
                commands.Register<PlaceSmokeFlakyOrderCommandHandler>();
                commands.UseUnitOfWork<SmokeDbContextUnitOfWork>();
            });
            liteBus.AddEvents(events =>
            {
                events.Register<SmokeOrderPlacedEvent>();
                events.Register<SmokeOrderProjectionHandler>();
                events.Register<SmokeFlakyOrderPlacedEvent>();
                events.Register<SmokeFlakyOrderProjectionHandler>();
            });
            liteBus.AddOutbox(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(ef =>
                {
                    ef.UseDbContext<SmokeDbContext>();
                    ef.EnableSaveChangesInterceptor();
                });
                outbox.Contracts.Register<SmokeOrderPlacedEvent>(OrderPlacedContract);
                outbox.Contracts.Register<SmokeFlakyOrderPlacedEvent>(FlakyOrderPlacedContract);
                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    Retry = new RetryOptions { InitialDelay = TimeSpan.Zero, UseJitter = false }
                });
                outbox.EnablePerHandlerDelivery();
                outbox.UseInProcessDispatch();
                outbox.EnableImmediateDispatch();
            });
        });
        // One registration: AddDbContextFactory supplies the singleton factory the stores use and the scoped context.
        services.AddDbContextFactory<SmokeDbContext>((serviceProvider, options) => options
            .UseNpgsql(connectionString)
            .AddLiteBusOutboxInterceptor(serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

        await using var provider = services.BuildServiceProvider(Smoke.DevelopmentValidation);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var orderId = Guid.NewGuid();
        await mediator.SendAsync(new PlaceSmokeOrderCommand(orderId)).ConfigureAwait(false);

        Smoke.Expect(
            recorder.Entries.Contains($"projected:{orderId:N}"),
            "the immediate handler's effect is visible when SendAsync returns");

        var row = await ReadOutboxRowAsync(connectionString, orderId).ConfigureAwait(false);
        Smoke.Expect(row is not null, "the immediate envelope is stored");
        Smoke.Expect(row!.Status == OutboxStatus.Published, "the immediate envelope ends published");
        Smoke.Expect(row.Handler == ProjectionSubscriberName, "the immediate envelope names the subscriber");

        var flakyOrderId = Guid.NewGuid();
        await mediator.SendAsync(new PlaceSmokeFlakyOrderCommand(flakyOrderId)).ConfigureAwait(false);

        Smoke.Expect(
            recorder.Entries.Contains($"flaky-projected:{flakyOrderId:N}") == false,
            "the failing immediate handler records nothing when SendAsync returns");

        var flakyRow = await ReadOutboxRowAsync(connectionString, flakyOrderId).ConfigureAwait(false);
        Smoke.Expect(flakyRow is not null, "the failed immediate envelope is stored");
        Smoke.Expect(flakyRow!.Status != OutboxStatus.Published, "the failed immediate envelope stays unpublished");
        Smoke.Expect(flakyRow.AttemptCount >= 1, "the failed immediate dispatch counts its attempt");

        var processor = provider.GetRequiredService<IOutboxProcessor>();
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        Smoke.Expect(
            recorder.Entries.Contains($"flaky-projected:{flakyOrderId:N}"),
            "a processor pass delivers the failed immediate envelope");

        var deliveredRow = await ReadOutboxRowAsync(connectionString, flakyOrderId).ConfigureAwait(false);
        Smoke.Expect(deliveredRow is not null, "the redelivered envelope is still stored");
        Smoke.Expect(deliveredRow!.Status == OutboxStatus.Published, "the redelivered envelope ends published");
    }

    /// <summary>
    ///     Reads the outbox row whose payload carries the given order identifier.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="orderId">The order identifier to look for.</param>
    /// <returns>The stored row, or <see langword="null" /> when no payload carries the identifier.</returns>
    private static async Task<OutboxMessageEntity?> ReadOutboxRowAsync(string connectionString, Guid orderId)
    {
        var marker = orderId.ToString();

        await using (var context = SmokeDbContext.Create(connectionString))
        {
            var rows = await context.OutboxMessages.ToListAsync().ConfigureAwait(false);
            return rows.FirstOrDefault(row => row.Payload.Contains(marker, StringComparison.Ordinal));
        }
    }
}

/// <summary>
///     A command that stages a placed-order event for immediate dispatch.
/// </summary>
/// <param name="OrderId">The order identifier written to the staged event.</param>
internal sealed record PlaceSmokeOrderCommand(Guid OrderId) : ICommand;

/// <summary>
///     A command that stages a flaky placed-order event for immediate dispatch.
/// </summary>
/// <param name="OrderId">The order identifier written to the staged event.</param>
internal sealed record PlaceSmokeFlakyOrderCommand(Guid OrderId) : ICommand;

/// <summary>
///     The placed-order event the immediate dispatch scenario stages and projects.
/// </summary>
internal sealed record SmokeOrderPlacedEvent : IEvent
{
    /// <summary>
    ///     Gets the order identifier the event carries.
    /// </summary>
    public Guid OrderId { get; init; }
}

/// <summary>
///     The flaky placed-order event the immediate dispatch scenario stages and projects.
/// </summary>
internal sealed record SmokeFlakyOrderPlacedEvent : IEvent
{
    /// <summary>
    ///     Gets the order identifier the event carries.
    /// </summary>
    public Guid OrderId { get; init; }
}

/// <summary>
///     Stages the placed-order event into the transactional outbox.
/// </summary>
internal sealed class PlaceSmokeOrderCommandHandler : ICommandHandler<PlaceSmokeOrderCommand>
{
    /// <summary>
    ///     The transactional outbox the handler stages its event through.
    /// </summary>
    private readonly ITransactionalOutbox<SmokeDbContext> _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlaceSmokeOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="outbox">The transactional outbox the handler stages its event through.</param>
    public PlaceSmokeOrderCommandHandler(ITransactionalOutbox<SmokeDbContext> outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(PlaceSmokeOrderCommand message, CancellationToken cancellationToken = default)
    {
        await _outbox.EnqueueAsync(
            OutboxEnqueueItem<SmokeOrderPlacedEvent>.From(new SmokeOrderPlacedEvent { OrderId = message.OrderId }),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     Stages the flaky placed-order event into the transactional outbox.
/// </summary>
internal sealed class PlaceSmokeFlakyOrderCommandHandler : ICommandHandler<PlaceSmokeFlakyOrderCommand>
{
    /// <summary>
    ///     The transactional outbox the handler stages its event through.
    /// </summary>
    private readonly ITransactionalOutbox<SmokeDbContext> _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlaceSmokeFlakyOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="outbox">The transactional outbox the handler stages its event through.</param>
    public PlaceSmokeFlakyOrderCommandHandler(ITransactionalOutbox<SmokeDbContext> outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(PlaceSmokeFlakyOrderCommand message, CancellationToken cancellationToken = default)
    {
        await _outbox.EnqueueAsync(
            OutboxEnqueueItem<SmokeFlakyOrderPlacedEvent>.From(new SmokeFlakyOrderPlacedEvent { OrderId = message.OrderId }),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
///     Projects the placed-order event as soon as its envelope is dispatched.
/// </summary>
[SubscriberName(ImmediateDispatchSmoke.ProjectionSubscriberName)]
[DispatchImmediately]
internal sealed class SmokeOrderProjectionHandler : IEventHandler<SmokeOrderPlacedEvent>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeOrderProjectionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public SmokeOrderProjectionHandler(SmokeRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(SmokeOrderPlacedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"projected:{message.OrderId:N}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Fails the first delivery of the flaky placed-order event, then projects it.
/// </summary>
[SubscriberName(ImmediateDispatchSmoke.FlakyProjectionSubscriberName)]
[DispatchImmediately]
internal sealed class SmokeFlakyOrderProjectionHandler : IEventHandler<SmokeFlakyOrderPlacedEvent>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Fails the first delivery and lets later deliveries through.
    /// </summary>
    private readonly SmokeFlakyDeliveryGate _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeFlakyOrderProjectionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    /// <param name="gate">Fails the first delivery and lets later deliveries through.</param>
    public SmokeFlakyOrderProjectionHandler(SmokeRecorder recorder, SmokeFlakyDeliveryGate gate)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(gate);
        _recorder = recorder;
        _gate = gate;
    }

    /// <inheritdoc />
    public Task HandleAsync(SmokeFlakyOrderPlacedEvent message, CancellationToken cancellationToken = default)
    {
        if (_gate.ShouldFail())
        {
            throw new InvalidOperationException("The smoke projection fails its first delivery.");
        }

        _recorder.Record($"flaky-projected:{message.OrderId:N}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Fails the first flaky delivery and lets later deliveries through.
/// </summary>
internal sealed class SmokeFlakyDeliveryGate
{
    /// <summary>
    ///     The number of deliveries still to fail.
    /// </summary>
    private int _failuresRemaining = 1;

    /// <summary>
    ///     Decides whether the current delivery fails.
    /// </summary>
    /// <returns><see langword="true" /> for the first delivery; otherwise, <see langword="false" />.</returns>
    public bool ShouldFail()
    {
        return Interlocked.Decrement(ref _failuresRemaining) >= 0;
    }
}
