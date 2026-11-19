# Domain Events and Unit of Work

Domain events let an aggregate record facts ("order placed") that other parts of the system react to. Collect events on the aggregate during command handling. Act on them at the transaction boundary, so persistence and publication stay aligned.

This page builds on [Reliable Messaging](README.md) and the [Event Module](../concepts/events.md).

For integration events that must commit with aggregate persistence, map domain facts to integration-shaped messages and enqueue through the **transactional** outbox API. See [Transactional Messaging Writes](transactional-writes.md).

## Domain Event Contracts

```csharp
public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

Aggregates can expose read-only events and keep mutation methods private.

```csharp
public abstract class AggregateRoot : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

public sealed record OrderPlaced(Guid OrderId, DateTime OccurredAtUtc) : IDomainEvent;
```

## Dispatch Timing

Dispatch events at one of four points.

| Timing | Use when | Trade-off |
| --- | --- | --- |
| Before commit | Handlers must join the same transaction | Handler failures roll back the command |
| After commit | Handlers can run after state is saved | Handler failures need retry logic |
| After commit, with the outbox as fallback | Reactions should run as soon as the commit returns, with processor redelivery when immediate dispatch misses | Requires per-handler delivery, idempotent reactions, and monitoring of the fallback rate |
| Outbox | Publication must survive process failure | Requires an outbox table and processor |

For events that leave the process or update state outside the current aggregate, the outbox is the safest default. Store the outbox row in the same transaction as the aggregate change. Publish it from a background worker. When reactions should also run as soon as the commit returns, dispatch them immediately after commit and let the outbox processor redeliver what immediate dispatch missed. See [Immediate dispatch](immediate-dispatch.md).

## EF Core Interceptor Shape

The application layer can collect events from tracked aggregates in a `SaveChangesInterceptor` or in a unit-of-work wrapper.

```csharp
public sealed class DomainEventCollector
{
    public IReadOnlyCollection<IDomainEvent> Collect(DbContext dbContext)
    {
        var aggregates = dbContext.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToArray();

        var events = aggregates.SelectMany(entity => entity.DomainEvents).ToArray();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return events;
    }
}
```

For cross-process integration events, map domain events to integration contracts and enqueue before commit:

```csharp
// EF: inject ITransactionalOutbox<AppDbContext>
// PostgreSQL: inject ITransactionalOutbox when EnableAmbientTransactionProvider() is configured
await transactionalOutbox.EnqueueAsync(
    OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
        new OrderSubmittedIntegrationEvent { OrderId = order.Id },
        OutboxEnqueueMetadata.Immediate with
        {
            Target = new PublicationTarget.Topic("orders"),
        }),
    cancellationToken).ConfigureAwait(false);

await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // EF
// or CommitAsync on your PostgreSQL unit of work
```

Do not use `IOutbox` inside a handler that already holds an open domain transaction. It commits separately. For in-process reactions only, publish through `IEventMediator` after `SaveChangesAsync` instead of the outbox.

## Harvesting Events from Tracked Aggregates

Copying events from aggregates to the collector by hand in every handler is repetitive and easy to forget. Storage harvesters do it automatically. `IDomainEventHarvester` is the replaceable port: its single method `HarvestInto(IDomainEventCollector collector)` moves the domain events of the entities a unit of work tracks into the collector. The staging completion handler registered by `OutboxModuleBuilder.StageCollectedEvents()` runs every registered harvester before it drains the collector, so harvested events stage with the same unit of work. Harvesters run only when the mediation completed with `MediationOutcome.Succeeded`; a failed mediation stages nothing.

Aggregates that implement `IHasDomainEvents` are harvested by default once a storage harvester is registered, without further configuration:

```csharp
public sealed class Order : IHasDomainEvents
{
    private readonly List<object> _domainEvents = [];

    public IReadOnlyCollection<object> DomainEvents => _domainEvents;

    public void Place()
    {
        _domainEvents.Add(new OrderPlaced(Id, DateTimeOffset.UtcNow));
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

An aggregate base type with its own event storage configures accessors on the outbox builder instead of implementing the contract:

```csharp
registry.AddOutbox(outbox => outbox
    .HarvestDomainEvents<Order>(
        order => order.PendingEvents,
        order => order.ClearPendingEvents()));
```

A configured accessor wins over the `IHasDomainEvents` default for its aggregate type, and applies to derived types and proxies through the configured base type. Registering the same aggregate type twice replaces the previous pair. A harvester clears the events it moved from each entity, so harvesting twice in one unit of work stages each event once, and a child mediation that harvests first leaves nothing for its parent to harvest again.

Aggregates built on one open generic base register once through the `Type` overload:

```csharp
registry.AddOutbox(outbox => outbox
    .HarvestDomainEvents(
        typeof(AggregateRoot<>),
        static a => ((AggregateRoot)a).DomainEvents,
        static a => ((AggregateRoot)a).ClearDomainEvents()));
```

An open generic definition matches every closed entity built on it, found up the base-type chain and among the implemented interfaces, so one registration covers every aggregate built on the same base without a marker interface. A closed type behaves like the generic overload. Aggregates with no common base register through a predicate:

```csharp
registry.AddOutbox(outbox => outbox
    .HarvestDomainEventsWhere(
        static t => typeof(IOrderEvents).IsAssignableFrom(t),
        static a => ((IOrderEvents)a).PendingEvents,
        static a => ((IOrderEvents)a).ClearPendingEvents()));
```

Type registrations and predicates share one ordered list. An exact type match wins first; otherwise the first registration in order whose test matches is used. Register broad conventions after the specific types they must not shadow. Registering the same predicate instance twice replaces the previous pair; different predicates accumulate.

Each storage adapter ships its harvester behind a `HarvestDomainEvents()` builder method. The Marten harvester reads the pending inserts, updates, and deletions of the scoped `IDocumentSession`. A deletion harvests only when it carries a document, as with `session.Delete(entity)`; `DeleteWhere` carries no document, so flows that delete by predicate collect their events on `IDomainEventCollector` by hand. The Entity Framework Core harvester reads the `ChangeTracker` entries of the configured `DbContext` in every state, so a loaded aggregate that raises an event without touching a mapped property still harvests. See [Outbox Marten Storage](../integrations/outbox-marten-storage.md) and [Outbox Entity Framework Core Storage](../integrations/outbox-ef-core-storage.md) for registration.

## Decide and Translate Before Staging

An aggregate records domain events in its own shape. The outbox publishes integration events in the contract other services read. Between harvest and stage, the staging handler decides what each drained event does and translates domain shape into published shape. Each module declares its own decisions on the outbox builder:

```csharp
registry.AddOutbox(outbox => outbox
    .StageCollectedEvents()
    .TranslateDomainEvent<OrderPlacedDomainEvent>(
        static e => [new OrderSubmittedIntegrationEvent(e.OrderId)])
    .IgnoreDomainEvent<LineTotalRecalculatedDomainEvent>()
    .RequireDomainEventDecision());
```

`TranslateDomainEvent` maps one domain event to zero or more messages to stage. `IgnoreDomainEvent` drops an event consumed inside the mediation, such as by a post-handler, that must never be published. `RequireDomainEventDecision` fails staging before the commit when a drained event has no registered decision, so an unregistered event type throws instead of publishing an internal event. A translation that needs scoped services uses the translator overload with an `IDomainEventTranslator<TEvent>` implementation:

```csharp
registry.AddOutbox(outbox => outbox
    .StageCollectedEvents()
    .UseAmbientTrace()
    .UseAmbientTenant()
    .RequireDomainEventDecision()
    .TranslateDomainEvent<DiagnosisRecordedDomainEvent, DiagnosisRecordedTranslator>());
```

`PublishDomainEventInProcess` publishes an event through `IEventMediator` after the commit instead of staging it, as a root mediation with a cause. See [Decide and Translate Before Staging](outbox.md#decide-and-translate-before-staging) for every decision, the one-decision-per-type conflict rule, the undecided policies, and the fault codes.

## Command Pipeline Boundary

Register the application's unit of work with `UseUnitOfWork<TUnitOfWork>` on the command module builder. LiteBus then commits it from a completion handler at `HandlerPriorities.UnitOfWork` after every successful mediation. The application implements `IUnitOfWork`, whose single `CommitAsync` method commits the work the handler staged. LiteBus resolves the unit of work from the mediation scope and calls it once per mediation that ended with `MediationOutcome.Succeeded`. LiteBus never opens or owns the transaction itself.

```csharp
public sealed class OrderUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _dbContext;

    public OrderUnitOfWork(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly)
    .UseUnitOfWork<OrderUnitOfWork>());

registry.AddQueries(queries => queries
    .RegisterFromAssembly(typeof(GetOrderQuery).Assembly)
    .UseUnitOfWork<OrderUnitOfWork>());
```

Do not place the commit in a post-handler. When the main handler throws, a post-handler never runs. A commit there cannot handle a failure. Everything LiteBus writes afterwards, including the audit record at `HandlerPriorities.Observability`, then lands outside the transaction.

The completion stage runs on every path. The shipped handler therefore gates on the outcome. A denial, a validation failure, a shortcut answer, a fault, and a cancellation all end without committing.

A shortcut answer reports success. It still does not commit, because the main handler never ran and staged nothing. When the commit throws, the mediation fails with that exception. A concurrency conflict therefore reaches the caller instead of silently losing the write.

When the store needs an explicit begin, implement `IUnitOfWorkBoundary` on the unit of work. LiteBus calls `BeginAsync` once per mediation, after guards, validators and shortcuts have let the message through and before the first pre-handler runs, and calls `RollbackAsync` when the mediation began and did not succeed. A store such as EF Core needs no explicit begin, because `SaveChangesAsync` is the commit. See [Unit of work](../catalog/mediator/unit-of-work.md).

Pre-handlers run only after guards, validators, and shortcuts have passed. A transaction opened in the pre-handler therefore commits only when the main handler and every post-handler ran without throwing. Use post-handlers for reactions inside the transaction (publish follow-up events, refresh projections). Keep the commit in the completion handler.

Calling `UseUnitOfWork` twice on one builder is a configuration error. You may register different types on commands and queries. Each axis then commits its own. Prefer one type, so the shared `IUnitOfWork` service resolves unambiguously.

When validation does not need database locks, run validation before opening a transaction. When publication must survive a process crash, persist domain events to an outbox in the same transaction as the aggregate change. See [The Handler Pipeline](../concepts/handler-pipeline.md) for the stage order and the completion rules.

To move the domain events a handler collected into the outbox before the commit, register `IDomainEventCollector` staging with `OutboxModuleBuilder.StageCollectedEvents()`. Staging runs at `HandlerPriorities.Persistence`, ahead of the unit of work. See [Domain Event Staging](outbox.md#domain-event-staging).

When commands run as a batch through `ICommandBatchMediator`, every command shares one dispatch scope, so each command's staging handler enqueues into the shared scope's `ITransactionalOutbox`. Staged domain events therefore commit with the batch's single commit and roll back when the batch rolls back. No registration change is needed.

## After-Commit Notifications Through the Outbox

Sending a buyer message inline inside the command transaction delays the money write until the mail provider responds. A mail-provider stall then blocks the money write, but the specification declares the message after-commit. Stage the message instead:

1. The handler records the consequence during its unit of work.
2. A completion handler writes it to the outbox before the commit.
3. The outbox processor dispatches it after commit with retry and deduplication.

LiteBus provides no `Defer` API. This recipe is the supported shape. A library collector is on the [roadmap](../roadmap/integrator-feedback.md#a-library-after-commit-collector).

The handler states its consequences instead of performing them, on the aggregate or in the execution context:

```csharp
public sealed record DeferredNotification(string Recipient, string Template, string OrderId);

public sealed record DeferredNotifications(IReadOnlyList<DeferredNotification> Items);

// In the command handler, after the aggregate change:
_context.Data.Set(new DeferredNotifications(
[
    new DeferredNotification(order.BuyerEmail, "refund-settled", order.Id.ToString())
]));
```

A completion handler ordered before the commit stages each consequence as an integration event through the transactional outbox, so the rows commit with the aggregate:

```csharp
[HandlerPriority(HandlerPriorities.ReservedCeiling)]
public sealed class StageNotificationsCompletionHandler<TCommand> : ICommandCompletionHandler<TCommand>
    where TCommand : ICommand
{
    private readonly IExecutionContext _context;
    private readonly ITransactionalOutbox _outbox;

    public StageNotificationsCompletionHandler(IExecutionContext context, ITransactionalOutbox outbox)
    {
        _context = context;
        _outbox = outbox;
    }

    public async Task HandleCompletionAsync(
        MessageCompletionContext<TCommand> context,
        CancellationToken cancellationToken)
    {
        if (context.Outcome != MediationOutcome.Succeeded)
        {
            return;
        }

        if (!_context.Data.TryGet<DeferredNotifications>(out var deferred))
        {
            return;
        }

        foreach (var notification in deferred.Items)
        {
            await _outbox.EnqueueAsync(
                OutboxEnqueueItem<BuyerNotifiedIntegrationEvent>.From(
                    new BuyerNotifiedIntegrationEvent(notification.OrderId, notification.Recipient, notification.Template)),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
```

`ReservedCeiling` sits below `HandlerPriorities.UnitOfWork`. It is the band for application infrastructure that runs after every LiteBus handler and still inside the transaction. The staged rows therefore join the commit the unit-of-work handler performs.

The `Succeeded` gate matters for the same reason the commit gate does. A failed mediation must stage nothing.

After commit, the outbox processor dispatches each event to its reaction. The reaction performs the send through the existing sender. The reaction stays idempotent, because at-least-once dispatch can deliver twice.

When a durable axis can redeliver the command itself, key each staged event with `WithIdempotency`. A retried mediation then stages no duplicate.

## CQS Rules

Command handlers can change state and publish domain events. Query handlers should read state only. A query handler should not depend on `ICommandMediator`, publish events, or write through repositories. When retries or durability are used, event handlers should be idempotent.

## Next

- Writer API and registration: [Transactional messaging writes](transactional-writes.md)
- Outbox tables: [PostgreSQL schema management](../integrations/postgresql-schema-management.md)
- Processors: [Hosted services](../architecture/hosted-services.md)
