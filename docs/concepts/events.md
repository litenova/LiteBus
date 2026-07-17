# Event Module

An event announces that something already happened: a product was created, an order shipped, a payment failed. This page covers the event side of LiteBus: how to define events (including without any interface), write handlers, publish, and control how many handlers run and in what concurrency. It is for a developer adding publish-subscribe behavior and assumes you have read the [Command Module](commands.md) and [Query Module](queries.md).

An event differs from a command in two ways. It can have zero, one, or many handlers, and it returns nothing. Publishing an event with no handlers is not an error; the publisher does not depend on who is listening. Name events in the past tense, because they describe facts: `ProductCreatedEvent`, `OrderShipped`, `PaymentFailedEvent`.

## Event Contracts

An event can be a marked type or a plain object.

### `IEvent`

Implement the `IEvent` marker when the type lives in your application layer and you want it clearly labeled as an event.

```csharp
public sealed record ProductCreatedEvent(Guid ProductId, string Name) : IEvent;
```

### POCO Events

LiteBus can publish any class or record without an interface. This matters for domain models: an aggregate can raise `OrderShipped` as a plain type, and the domain layer stays free of any reference to LiteBus.

```csharp
// Lives in the domain layer, depends on nothing from LiteBus.
public sealed record OrderShipped(Guid OrderId, string TrackingNumber, DateTime ShippedAt);
```

Publish a POCO through the generic overload, `PublishAsync<TEvent>(TEvent @event, ...)`. Publish an `IEvent` through either overload.

## Event Handlers

A handler reacts to an event. Any number of handlers can subscribe to the same event, each with one responsibility.

```csharp
public sealed class SendConfirmationEmail : IEventHandler<OrderShipped>
{
    private readonly IEmailSender _email;

    public SendConfirmationEmail(IEmailSender email) => _email = email;

    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken = default)
        => _email.SendShippingConfirmationAsync(@event.OrderId, @event.TrackingNumber, cancellationToken);
}

public sealed class UpdateInventoryProjection : IEventHandler<OrderShipped>
{
    private readonly IProjectionStore _projections;

    public UpdateInventoryProjection(IProjectionStore projections) => _projections = projections;

    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken = default)
        => _projections.MarkShippedAsync(@event.OrderId, cancellationToken);
}
```

Both handlers run when `OrderShipped` is published. Order and concurrency are controlled per call; see the mediation settings below.

## Publishing

Inject `IEventMediator` to publish domain events to in-process handlers immediately.

```csharp
public interface IEventMediator
{
    Task PublishAsync(IEvent @event, EventMediationSettings? settings = null, CancellationToken cancellationToken = default);
    Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? settings = null, CancellationToken cancellationToken = default) where TEvent : notnull;
}
```

A common pattern is a command handler that publishes a domain event after it commits its work:

```csharp
public sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
{
    private readonly IEventMediator _eventMediator;
    private readonly IOrderRepository _orders;

    public ShipOrderCommandHandler(IEventMediator eventMediator, IOrderRepository orders)
    {
        _eventMediator = eventMediator;
        _orders = orders;
    }

    public async Task HandleAsync(ShipOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetAsync(command.OrderId, cancellationToken);
        var tracking = order.Ship();
        await _orders.SaveAsync(order, cancellationToken);

        await _eventMediator.PublishAsync(new OrderShipped(order.Id, tracking, DateTime.UtcNow), cancellationToken);
    }
}
```

For an in-memory publish, every handler runs in the same call before `PublishAsync` returns. To publish reliably across a transaction or process boundary instead, store the event and publish it after commit; see [Outbox](../reliable-messaging/outbox.md) and [Domain Events and Unit of Work](domain-events-and-unit-of-work.md).

## Controlling Execution

`EventMediationSettings` gives per-call control over which handlers run and how they run concurrently. It has two parts: `Routing` selects handlers, `Execution` decides concurrency.

### Execution: Priority Groups and Concurrency

Handlers are grouped by their `[HandlerPriority]` value, lowest first. Two independent switches decide concurrency: how the groups run relative to each other, and how handlers inside one group run.

```csharp
var settings = new EventMediationSettings
{
    Execution = new EventExecutionSettings
    {
        // Across priority groups. Sequential (default): group 1 finishes before group 2 starts.
        // Parallel: all groups run concurrently.
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,

        // Within one priority group. Sequential (default): handlers run one by one.
        // Parallel: handlers in the group run concurrently.
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
    }
};

await _eventMediator.PublishAsync(myEvent, settings);
```

The two switches compose. Sequential groups with parallel handlers gives ordered phases where each phase fans out: phase one (priority 0) runs all its handlers in parallel and completes, then phase two (priority 10) begins. When priority groups run in parallel, priority becomes grouping metadata only; a later group can finish before an earlier one. Ordering is explained in full on [Handler Priority](handler-priority.md).

#### Concurrency and Fault-Mode Decision Table

Use the table below when choosing `EventExecutionSettings` for a publish call. Defaults are sequential groups, sequential handlers within a group, and `ParallelFaultMode.PropagateFirst`.

| Priority groups | Handlers in same group | Parallel fault mode | Execution shape | Ordering guarantee | Failure behavior |
| --- | --- | --- | --- | --- | --- |
| Sequential | Sequential | n/a | Strict pipeline | Full handler order preserved | First fault stops the stage |
| Sequential | Parallel | PropagateFirst | Phased fan-out | Groups run in priority order; within-group order not guaranteed | First handler fault cancels sibling tasks in the group |
| Sequential | Parallel | AggregateAll | Phased fan-out | Groups run in priority order; within-group order not guaranteed | All handlers in the group run; failures aggregate into one exception |
| Parallel | Sequential | n/a | Concurrent groups | Priority order not guaranteed across groups | First fault stops the stage |
| Parallel | Parallel | PropagateFirst | Maximum concurrency | No order guarantees | First fault in a parallel await cancels siblings in that await |
| Parallel | Parallel | AggregateAll | Maximum concurrency | No order guarantees | All parallel tasks run; failures aggregate |

`ParallelFaultMode` applies only when handlers execute with `ConcurrencyMode.Parallel` at the priority-group level, within the same priority group, or both. Sequential stages always stop at the first unhandled fault regardless of fault mode.

### Routing: Selecting Handlers

`Routing` decides which registered handlers participate in this publish.

```csharp
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        Tags = new[] { "Notifications" },
        HandlerPredicate = descriptor => descriptor.HandlerType.Namespace!.StartsWith("MyProject.Core")
    }
};
```

`HandlerPredicate` receives the full `IHandlerDescriptor`, so you can filter on handler type, priority, tags, or message type, and it applies to both publish overloads. Tag and predicate filtering are covered on [Handler Filtering](handler-filtering.md).

## Shared Features

These mechanics apply to commands, queries, and events. Each has a dedicated page:

- [Handler Priority](handler-priority.md): group and order event handlers; the basis of the concurrency model above.
- [Handler Filtering](handler-filtering.md): select handlers per publish.
- [Execution Context](execution-context.md): share state across handlers in one publish.
- [Polymorphic Dispatch](polymorphic-dispatch.md): a handler for a base event type runs for every derived event.
- [Generic Messages & Handlers](generic-messages-and-handlers.md) and [Open Generic Handlers](open-generic-handlers.md): reusable handlers and cross-cutting steps such as auditing.

## Best Practices

- **Immutable events.** An event is a fact; model it as a record or `init`-only type so it cannot be altered after publication.
- **Idempotent handlers.** Events delivered through the outbox can arrive more than once. Design handlers to tolerate reprocessing the same event.
- **One responsibility per handler.** Keep each handler focused; add a handler rather than growing one.
- **Avoid deep event chains.** A handler that publishes another event that publishes another is hard to trace. For multi-step workflows, model the process explicitly rather than chaining handlers.

## Next
