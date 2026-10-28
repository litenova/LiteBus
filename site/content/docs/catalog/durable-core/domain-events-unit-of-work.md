# Domain Events and Unit of Work Contract

**ID:** `durable-core.domain-events-unit-of-work`
**Maturity:** GA

## Summary

Aggregates can record application-owned domain events during business operations. The application drains and maps those facts to integration contracts at the transaction boundary, then stages them through `ITransactionalOutbox` before committing domain state.

This page is the contract companion to [Domain Events and Unit of Work](../../reliable-messaging/domain-events-and-unit-of-work.md), which carries the collector shape, transaction boundary steps, timing choices, and pipeline guidance. What remains here is the public surface, package mapping, invariants, observability, and test coverage behind that guide.

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

## Domain Event Staging

`OutboxModuleBuilder.StageCollectedEvents()` registers the default scoped `IDomainEventCollector` and a completion handler that drains the collector into `ITransactionalOutbox` when a mediation completes successfully, ordered at `HandlerPriorities.Persistence` before the application commit at `HandlerPriorities.UnitOfWork`, so staged rows join the unit of work that produced them. A handler records consequences with `Collect` instead of publishing inline; `Drain` returns every collected event destructively in collection order, so a retried mediation stages only what was collected after the previous drain.

```csharp
registry.AddOutbox(outbox =>
{
    outbox.StageCollectedEvents();
});
```

Staging runs only on `MediationOutcome.Succeeded`: a failed mediation stages nothing. The staging handler resolves `ITransactionalOutbox` lazily and only when the drain is non-empty, so mediations that collect nothing never touch it. The application must register a scoped non-generic `ITransactionalOutbox` (the ambient PostgreSQL provider, the Entity Framework Core interceptor adapter, or the Marten writer); collecting events without one fails staging with a descriptive error. Entity Framework Core registers the non-generic adapter over `ITransactionalOutbox<TContext>` only when `EnableSaveChangesInterceptor()` is set. When `UseAmbientTrace` is also enabled, staged envelopes stamp ambient trace.

## Public Surface

| Surface | Role |
| --- | --- |
| `ITransactionalOutbox` | Stages outbox envelopes through a store bound to the caller's transaction |
| `ITransactionalOutbox<TContext>` | Stages envelopes through an existing EF Core context |
| `IDomainEventCollector` | Scoped per mediation; `Collect` records a domain event and `Drain` returns every collected event destructively |
| `OutboxModuleBuilder.StageCollectedEvents()` | Registers the collector and the staging completion handler at `HandlerPriorities.Persistence` |
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
- `DomainEventStagingTests.PublishAsync_should_stage_collected_events_on_success` verifies collected events drain into `ITransactionalOutbox` when the mediation succeeds, and `PublishAsync_should_stage_nothing_on_failure` verifies a failed mediation stages nothing.
- `DomainEventStagingTests.Staging_should_run_before_the_unit_of_work_band` verifies the staging completion handler runs at `HandlerPriorities.Persistence`, ahead of the application commit.
- `DomainEventStagingTests.PublishAsync_without_collected_events_should_not_require_a_transactional_outbox` verifies an empty drain never touches the outbox, and `PublishAsync_with_collected_events_but_no_transactional_outbox_should_throw` verifies collected events without a registered outbox fail with an error naming `StageCollectedEvents`.
- `EfCoreOutboxTransactionalIntegrationTests` verifies aggregate data and outbox rows commit or roll back together through one EF Core context.
- `PostgreSqlOutboxTransactionalIntegrationTests` verifies domain writes and outbox rows share one PostgreSQL transaction.
- `EfCoreOutboxProcessorEndToEndTests` verifies a committed integration event reaches the configured event handler through the outbox processor.

The command pre-handler and post-handler transaction wrapper remains an application composition choice. The atomic EF Core and PostgreSQL tests cover the underlying commit contract.

## Related Documentation

- [Domain Events and Unit of Work](../../reliable-messaging/domain-events-and-unit-of-work.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Event Module](../../concepts/events.md)
