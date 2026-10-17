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

Open the unit of work from a command pre-handler and commit it from a completion handler at `HandlerPriorities.UnitOfWork`, gated on `MessageCompletionContext.Outcome`. The commit does not belong in a post-handler: a post-handler never runs when the main handler throws, so a commit placed there cannot decide anything about a failure, and everything LiteBus writes afterwards (including the audit record at `HandlerPriorities.Observability`) lands outside the transaction.

```csharp
public sealed class TransactionCommandPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    private readonly OrderUnitOfWork _unitOfWork;

    public TransactionCommandPreHandler(OrderUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        return _unitOfWork.BeginAsync(cancellationToken);
    }
}

[HandlerPriority(HandlerPriorities.UnitOfWork)]
public sealed class TransactionCommandCompletionHandler<TCommand> : ICommandCompletionHandler<TCommand>
    where TCommand : ICommand
{
    private readonly OrderUnitOfWork _unitOfWork;

    public TransactionCommandCompletionHandler(OrderUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task HandleCompletionAsync(
        MessageCompletionContext<TCommand> context,
        CancellationToken cancellationToken)
    {
        if (context.Outcome == MediationOutcome.Succeeded)
        {
            return _unitOfWork.CommitAsync(cancellationToken);
        }

        return _unitOfWork.RollbackAsync(cancellationToken);
    }
}
```

The completion stage runs on every path, so the gate matters: a failed mediation must roll back rather than commit. Pre-handlers run only after guards, validators, and shortcuts have passed, so a transaction opened in the pre-handler commits only when the main handler and every post-handler ran without throwing. Use post-handlers for reactions inside the transaction (publish follow-up events, refresh projections); the commit stays in the completion handler.

Run validation before opening a transaction when validation does not need database locks. Persist domain events to an outbox in the same transaction as the aggregate change when publication matters after a process crash. See [The Handler Pipeline](../concepts/handler-pipeline.md) for the stage order and the completion rules.

## CQS Rules

Command handlers can change state and publish domain events. Query handlers should read state only. A query handler should not depend on `ICommandMediator`, publish events, or write through repositories. Event handlers should be idempotent when retries or durability are used.

## Next

- Writer API and registration: [Transactional messaging writes](transactional-writes.md)
- Outbox tables: [PostgreSQL schema management](../integrations/postgresql-schema-management.md)
- Processors: [Hosted services](../architecture/hosted-services.md)
