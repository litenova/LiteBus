# Domain Events and Unit of Work

**ID:** `durable-core.domain-events-unit-of-work`
**Maturity:** GA

## Summary

Aggregates can record application-owned domain events during business operations. The application drains and maps those facts to integration contracts at the transaction boundary, then stages them through `ITransactionalOutbox` before committing domain state.

## Domain Boundary

LiteBus does not define `IDomainEvent`, `IHasDomainEvents`, or an aggregate base class. These types belong to the application domain and do not require a LiteBus reference.

```csharp
public interface IDomainEvent;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    IReadOnlyList<IDomainEvent> DrainDomainEvents();
}
```

An aggregate records a fact such as `OrderPlacedDomainEvent`. It does not enqueue an outbox message itself. This keeps broker routes, contract versions, tenant metadata, and idempotency policy outside the domain model.

## Transaction Boundary

The application handler or unit-of-work component performs four operations in one transaction:

1. Mutate and persist the aggregate.
2. Drain recorded domain events.
3. Map each domain event to a registered integration contract.
4. Enqueue through the transactional outbox before commit.

```csharp
order.Place();

foreach (var domainEvent in collector.Drain(order))
{
    var item = mapper.Map(domainEvent);
    await transactionalOutbox.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
}

await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
```

Use `ITransactionalOutbox<TContext>` with an existing EF Core context. Use `ITransactionalOutbox` with a bound PostgreSQL connection and transaction. `IOutbox` writes through its own store boundary and must not replace a transactional writer inside an open domain transaction.

## Mapping

The mapper owns integration-specific metadata. For example, an order mapping can derive a stable idempotency key from the aggregate identifier and route the contract to `orders.events`:

```csharp
return OutboxEnqueueItem<OrderPlacedIntegrationEvent>.From(
    new OrderPlacedIntegrationEvent { OrderId = placed.OrderId },
    OutboxEnqueueMetadata.Immediate with
    {
        Idempotency = new Idempotency.Keyed($"order-placed:{placed.OrderId:N}"),
        Target = new PublicationTarget.Topic("orders.events")
    });
```

Mapping at this boundary also lets the application copy correlation, causation, and tenant metadata from the command context without adding those concerns to the domain event.

## Timing Choices

| Timing | Suitable Use | Failure Behavior |
| --- | --- | --- |
| Before commit | Local handler must participate in the same transaction | Handler failure rolls back the command |
| After commit | Local reaction can be retried by application code | State is already committed when the handler fails |
| Transactional outbox | Notification must survive process loss or cross a process boundary | Domain row and outbox row commit or roll back together; publication is asynchronous |

Use `IEventMediator` after commit for local reactions that do not require durable delivery. Use the transactional outbox for cross-process integration events and local work that must survive host failure.

## Public Surface

| Surface | Role |
| --- | --- |
| `ITransactionalOutbox` | Stages outbox envelopes through a store bound to the caller's transaction |
| `ITransactionalOutbox<TContext>` | Stages envelopes through an existing EF Core context |
| `OutboxEnqueueItem<TEvent>` | Carries one typed integration event and its durable metadata |
| `OutboxEnqueueMetadata` | Defines identity, idempotency, visibility, trace, tenant, and publication target |
| `IEventMediator` | Publishes non-durable in-process events after commit |

## Packages

| Package | Role |
| --- | --- |
| Application domain | Aggregate and domain event types |
| Application layer | Collector, mapper, and unit-of-work orchestration |
| `LiteBus.Outbox.Abstractions` | Transactional writer and enqueue contracts |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF Core transactional staging |
| `LiteBus.Outbox.Storage.PostgreSql` | Bound or ambient PostgreSQL transactional staging |
| `LiteBus.Events` | Optional after-commit in-process mediation |

## Invariants

- Domain event types remain application-owned.
- The application maps domain events to registered integration contracts before enqueue.
- The outbox row must join the same transaction as the aggregate write when atomicity is required.
- Drained events are cleared once they have been staged in the current unit of work.
- Outbox publication is asynchronous and at-least-once, so downstream handlers must be idempotent.
- Query handlers do not mutate aggregates, drain events, or enqueue messages.

## Non-Goals

- Automatic aggregate scanning across every persistence framework.
- Generated mappings from domain events to integration contracts.
- Event sourcing storage.
- Synchronous broker publication inside the domain transaction.

## Observability

Domain event collection has no LiteBus meter because it is application-owned. After commit, staged integration events appear in `litebus.outbox.queue.depth`. Successful background publication increments `litebus.outbox.processor.published`. An event that is never drained does not reach either signal, so applications can add a collector-specific counter when this failure mode needs monitoring.

## Test Coverage

- `DomainEventOutboxPatternTests.HandleAsync_WithRecordedDomainEvent_ShouldDrainMapAndEnqueueIntegrationEvent` defines an aggregate buffer, explicit collector, explicit mapper, handler, and bound transactional writer. It verifies the aggregate buffer is cleared and the stored envelope carries the registered contract, topic, deterministic idempotency key, and mapped payload.
- `EfCoreOutboxTransactionalIntegrationTests` verifies aggregate data and outbox rows commit or roll back together through one EF Core context.
- `PostgreSqlOutboxTransactionalIntegrationTests` verifies domain writes and outbox rows share one PostgreSQL transaction.
- `EfCoreOutboxProcessorEndToEndTests` verifies a committed integration event reaches the configured event handler through the outbox processor.

The command pre-handler and post-handler transaction wrapper remains an application composition choice. The atomic EF Core and PostgreSQL tests cover the underlying commit contract.

## Related Documentation

- [Domain Events and Unit of Work](../../concepts/domain-events-and-unit-of-work.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Event Module](../../concepts/events.md)
