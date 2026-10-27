# Domain Events and Unit of Work

Domain events let an aggregate record facts ("order placed") that other parts of the system react to. Collect events on the aggregate during command handling, then act on them at the transaction boundary so persistence and publication stay aligned. This page builds on [Reliable Messaging](README.md) and the [Event Module](../concepts/events.md).

For integration events that must commit with aggregate persistence, map domain facts to integration-shaped messages and enqueue through the **transactional** outbox API. See [Transactional messaging writes](transactional-writes.md).

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

There are three common timing choices.

| Timing | Use when | Trade-off |
| --- | --- | --- |
| Before commit | Handlers must join the same transaction | Handler failures roll back the command |
| After commit | Handlers can run after state is saved | Handler failures need retry logic |
| Outbox | Publication must survive process failure | Requires an outbox table and processor |

The outbox is the safest default for events that leave the process or update state outside the current aggregate. Store the outbox row in the same transaction as the aggregate change, then publish from a background worker.

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

Do not use `IOutbox` inside a handler that already holds an open domain transaction; it commits separately. For in-process reactions only, publish through `IEventMediator` after `SaveChangesAsync` instead of the outbox.

## Command Pipeline Boundary

Register the application's unit of work with `UseUnitOfWork<TUnitOfWork>` on the command module builder, and LiteBus commits it from a completion handler at `HandlerPriorities.UnitOfWork` after every successful mediation. The application implements `IUnitOfWork`, whose single `CommitAsync` method commits the work the handler staged; LiteBus resolves it from the mediation scope and calls it once per mediation that ended with `MediationOutcome.Succeeded`, and never opens or owns the transaction itself.

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

The commit does not belong in a post-handler: a post-handler never runs when the main handler throws, so a commit placed there cannot decide anything about a failure, and everything LiteBus writes afterwards (including the audit record at `HandlerPriorities.Observability`) lands outside the transaction. The completion stage runs on every path, so the shipped handler gates on the outcome: a denial, a validation failure, a shortcut answer, a fault, and a cancellation all end without committing. A shortcut answer does not commit even though it reports success, because the main handler never ran and staged nothing. A commit that throws fails the mediation with that exception, so a concurrency conflict reaches the caller instead of silently losing the write.

When the store needs an explicit begin, open it from a command pre-handler through the same scoped unit of work; a store such as EF Core needs none, because `SaveChangesAsync` is the commit. Pre-handlers run only after guards, validators, and shortcuts have passed, so a transaction opened in the pre-handler commits only when the main handler and every post-handler ran without throwing. Use post-handlers for reactions inside the transaction (publish follow-up events, refresh projections); the commit stays in the completion handler. Calling `UseUnitOfWork` twice on one builder is a configuration error; registering it on commands and queries with different types is allowed, with each axis committing its own, but prefer one type so the shared `IUnitOfWork` service resolves unambiguously.

Run validation before opening a transaction when validation does not need database locks. Persist domain events to an outbox in the same transaction as the aggregate change when publication matters after a process crash. See [The Handler Pipeline](../concepts/handler-pipeline.md) for the stage order and the completion rules.

To move the domain events a handler collected into the outbox before the commit, register `IDomainEventCollector` staging with `OutboxModuleBuilder.StageCollectedEvents()`; it runs at `HandlerPriorities.Persistence`, ahead of the unit of work. See [Domain Event Staging](outbox.md#domain-event-staging).

## After-Commit Notifications Through the Outbox

A buyer message sent inline inside the command transaction holds a money write hostage to a mail-provider stall, yet the specification declares the message after-commit. Stage it instead: the handler records the consequence during its unit of work, a completion handler writes it to the outbox before the commit, and the outbox processor dispatches it after commit with retry and deduplication. There is no `Defer` API; this recipe is the supported shape, and a library collector stays on the [roadmap](../roadmap/integrator-feedback.md#a-library-after-commit-collector).

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

`ReservedCeiling` sits below `HandlerPriorities.UnitOfWork`, which is the band for application infrastructure that runs after every LiteBus handler and still inside the transaction, so the staged rows join the commit the unit-of-work handler performs. The `Succeeded` gate matters for the same reason the commit's does: a failed mediation must stage nothing. After commit, the outbox processor dispatches each event to its reaction, and the reaction performs the send through the existing sender; the reaction stays idempotent, because at-least-once dispatch can deliver twice. When the command itself can be redelivered by a durable axis, key each staged event with `WithIdempotency` so a retried mediation stages no duplicate.

## CQS Rules

Command handlers can change state and publish domain events. Query handlers should read state only. A query handler should not depend on `ICommandMediator`, publish events, or write through repositories. Event handlers should be idempotent when retries or durability are used.

## Next

- Writer API and registration: [Transactional messaging writes](transactional-writes.md)
- Outbox tables: [PostgreSQL schema management](../integrations/postgresql-schema-management.md)
- Processors: [Hosted services](../architecture/hosted-services.md)
