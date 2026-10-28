# Event Module

An event announces that something already happened. This page explains how to define an event, handle it, publish it, and control how its handlers run.

> **This page is for** developers who add publish-subscribe behavior.
>
> **After reading it** you can publish events and tune handler concurrency. Continue with [The Handler Pipeline](handler-pipeline.md).

## Define an Event

```csharp
public sealed record ProductCreatedEvent(Guid ProductId, string Name) : IEvent;

// Lives in the domain layer and depends on nothing from LiteBus.
public sealed record OrderShipped(Guid OrderId, string TrackingNumber, DateTime ShippedAt);
```

An event can be a marked type or a plain object. Implement the `IEvent` marker when the type lives in your application layer and you want it labeled as an event. Skip the marker for domain models. LiteBus publishes any class or record through the generic overload, so an aggregate stays free of LiteBus references.

Two rules define an event. It can have zero, one, or many handlers, and it returns nothing. Publishing an event with no handlers succeeds by default. Publishing code never depends on which handlers listen. Name events in the past tense because they describe facts, such as `ProductCreatedEvent` or `OrderShipped`.

The pipeline roles accept plain events too. Guards, validators, shortcuts, pre-handlers, post-handlers, error handlers, and completion handlers all constrain `TEvent` to `notnull`, so an unmarked domain event runs through the same full pipeline.

## Handle an Event

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

A handler reacts to an event with one responsibility. Both handlers above run when `OrderShipped` is published. Keep each handler focused and add a handler rather than growing one.

Keep handlers idempotent, because events stored through the outbox can arrive more than once. Avoid deep chains in which each handler publishes the next event. Model multi-step workflows explicitly instead.

### One Handler for Several Events

One class can implement several closed handler contracts when one reaction answers more than one event:

```csharp
public sealed class ReleaseRefundedInventory
    : IEventHandler<OrderRefundSucceeded>, IEventHandler<OrderRefundSettledOutsideProcessor>
{
    public Task HandleAsync(OrderRefundSucceeded @event, CancellationToken cancellationToken = default)
        => ReleaseAsync(@event.OrderId, cancellationToken);

    public Task HandleAsync(OrderRefundSettledOutsideProcessor @event, CancellationToken cancellationToken = default)
        => ReleaseAsync(@event.OrderId, cancellationToken);

    private Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken) => ...;
}
```

Registration records one main handler per closed contract. The mediator calls each through the contract its registration names, including when the event is published as `IEvent` or through a base-typed reference. The class needs no untyped entry point. `IMessageHandler` is a marker interface with no members.

## Publish an Event

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

Inject `IEventMediator` to publish to in-process handlers. The signatures are:

```csharp
Task PublishAsync(IEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default);
Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? eventMediationSettings = null, CancellationToken cancellationToken = default) where TEvent : notnull;
```

Publish an `IEvent` through either overload and a plain object through the generic one. An in-memory publish runs every handler in the same call before `PublishAsync` returns. Set `ThrowIfNoHandlerFound` to fail a publish with no subscribers. Leave the default `false` when a publish with no subscribers is acceptable.

Set `AutoRegisterUnregisteredMessageTypes` to register a plain unregistered event type in the message registry on first publish:

```csharp
var settings = new EventMediationSettings
{
    AutoRegisterUnregisteredMessageTypes = true
};

await eventMediator.PublishAsync(new OrderShipped(orderId, tracking, shippedAt), settings, cancellationToken);
```

The default is `false`, and the setting is independent of `ThrowIfNoHandlerFound`. It controls registration, not whether a publish with no subscribers fails. It covers plain-object events that were never registered, so a domain event reaches the pipeline without explicit registration. It is not a substitute for assembly registration. Registered handlers, pipeline roles, and metadata still come from `RegisterFromAssembly` or explicit `Register` calls.

To publish across a transaction or process boundary instead, store the event and publish it after commit. See [Outbox](../reliable-messaging/outbox.md) and [Domain Events and Unit of Work](../reliable-messaging/domain-events-and-unit-of-work.md).

## Control How Handlers Run

```csharp
var settings = new EventMediationSettings
{
    Execution = new EventExecutionSettings
    {
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
    }
};

await eventMediator.PublishAsync(myEvent, settings, cancellationToken);
```

Handlers group by their `[HandlerPriority]` value, lowest first. Two settings decide concurrency. One governs how priority groups run relative to each other, and the other governs how handlers inside one group run. Both default to `ConcurrencyMode.Sequential`.

Sequential groups with parallel handlers form ordered phases, and the handlers in each phase fan out. Phase one finishes, then phase two begins. When groups run in parallel, priority only groups handlers, and a later group can finish before an earlier one. See [Handler Priority](handler-priority.md).

## Isolate Failures Without Adding Concurrency

```csharp
var settings = new EventMediationSettings
{
    Execution = new EventExecutionSettings
    {
        FaultMode = HandlerFaultMode.ContinueAndAggregate
    }
};

await eventMediator.PublishAsync(orderConfirmed, settings, cancellationToken);
```

`HandlerFaultMode` decides fault isolation and applies to sequential and parallel execution alike, so isolating faults never forces you to run handlers in parallel. That matters when handlers share a resource that is not thread-safe, such as an ORM session. Those handlers must stay sequential, and stopping at the first failure would silently skip the subscribers after it. The default is `HandlerFaultMode.PropagateFirst`.

| Groups | Handlers in group | Fault mode | Failure behavior |
| --- | --- | --- | --- |
| Sequential | Sequential | PropagateFirst | First fault stops the group, and handlers behind it do not run |
| Sequential | Sequential | ContinueAndAggregate | Every handler in the group runs, and failures aggregate into one exception |
| Sequential | Parallel | PropagateFirst | One fault is reported after started siblings in the group settle |
| Sequential | Parallel | ContinueAndAggregate | All handlers in the group run, and failures aggregate into one exception |
| Parallel | Parallel | ContinueAndAggregate | All parallel tasks run, and failures aggregate |

The mode governs handlers inside one priority group only. When groups run sequentially, a later group does not run after an earlier one failed, because the priority difference states that the later group runs after the earlier one.

Neither mode cancels sibling work. Cancellation stays cooperative through the token you pass to `PublishAsync`. Under `ContinueAndAggregate`, when every collected failure comes from cancellation, the publish reports `Canceled` by rethrowing `OperationCanceledException` instead of aggregating. Mixed cancellation and faults still aggregate into one exception.

## Select Which Handlers Run

```csharp
var settings = new EventMediationSettings
{
    Routing = new EventRoutingSettings
    {
        Tags = ["Notifications"]
    }
};

await eventMediator.PublishAsync(myEvent, settings, cancellationToken);
```

`Routing` decides which registered handlers join this publish. `Tags` keeps handlers with a matching tag. Handlers without tags always run. Add a handler predicate for filtering beyond tags. See [Handler Filtering](handler-filtering.md).

## Details

- See [The Handler Pipeline](handler-pipeline.md) for the full stage order for events. That page is authoritative when the two pages seem to differ.
- `EventModuleBuilder.RegisterFromAssembly` discovers pipeline roles the same way it discovers handlers. It also registers an `IMessageDefinition<TEvent, TValue>` written for an unmarked event, so a plain event can carry the audit or idempotency metadata declared for it.

## Next

- [The Handler Pipeline](handler-pipeline.md) for the stages every axis shares
- [Outbox](../reliable-messaging/outbox.md) for reliable publication after commit
- [Handler Priority](handler-priority.md) for ordering and the concurrency model
