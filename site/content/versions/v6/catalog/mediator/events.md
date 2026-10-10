# Events

- **ID**: `mediator.events`
- **Name**: Events
- **Maturity**: GA
- **Summary**: Broadcasts in-process events to zero or many handlers with configurable routing and concurrency.

## What It Does

`IEventMediator` publishes events through `AsyncBroadcastMediationStrategy`. Events can have zero, one, or many handlers. Pre-handlers run before broadcast. Main handlers are grouped by `[HandlerPriority]` and executed according to `EventExecutionSettings`. Post and error stages wrap the broadcast flow.

`EventMediationSettings.ThrowIfNoHandlerFound` controls whether empty subscriptions are allowed. `AutoRegisterUnregisteredMessageTypes` allows publishing plain event types not pre-registered in the message module.

## Public Surface

```csharp
public sealed record ProductCreatedEvent(Guid Id) : IEvent;

public sealed class SendEmailOnProductCreated : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

await eventMediator.PublishAsync(new ProductCreatedEvent(Guid.NewGuid()), cancellationToken);
```

| API | Role |
| --- | --- |
| `IEvent` | Marker event contract |
| `IEventMediator.PublishAsync(IEvent, EventMediationSettings?, CancellationToken)` | Publish interface-marked event |
| `IEventMediator.PublishAsync<TEvent>(TEvent, EventMediationSettings?, CancellationToken)` | Publish plain or typed event |
| `IEventHandler<TEvent>` | Main broadcast handler |
| `IEventPreHandler<TEvent>` | Pre stage |
| `IEventPostHandler<TEvent>` | Post stage |
| `IEventErrorHandler<TEvent>` | Error stage |
| `EventMediatorExtensions.PublishAsync(..., string tag, ...)` | Single-tag routing sugar |

## Packages

- `LiteBus.Events`
- `LiteBus.Events.Abstractions`
- `LiteBus.Messaging` (transitive runtime dependency)

## Requires

- `mediator.module-registration`
- `mediator.handler-pipeline`
- `mediator.handler-priority`
- `mediator.mediation-settings`

## Invariants

- Event publish accepts zero handlers unless `ThrowIfNoHandlerFound = true`.
- Handler ordering is deterministic only in sequential modes.
- `ArgumentNullException` is thrown for `null` event arguments.
- Parallel failure behavior follows `ParallelFaultMode` (`PropagateFirst` or `AggregateAll`).

## Non-Goals

- Durable event delivery guarantees (outbox provides that axis).
- Cross-process publish/subscribe transport delivery.
- Built-in replay buffer or event sourcing store.

## Observability

No dedicated event mediator meter or activity source exists.

Operational alternatives:
- Log in event handlers and pipeline stages.
- Use durable outbox and transport telemetry for persisted or broker-backed event paths.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `mediating_simple_event_goes_through_registered_handlers_correctly` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_priority_handlers_executes_in_correct_order` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_parallel_priority_groups_executes_all_handlers_concurrently` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_no_handlers_should_throw_exception_when_ThrowIfNoHandlerFound_is_true` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_handler_predicate_filter_executes_only_matching_handlers` | `LiteBus.Mediator.UnitTests` |
| `PublishAsync_WithNullEvent_ThrowsArgumentNullException` | `LiteBus.Mediator.UnitTests` |

### Untested

- Load-heavy parallel event publish with many priority groups and `AggregateAll`.
- Plain event auto-registration across very large assemblies.

### Out-of-Scope

- Broker dispatch, retries, dead-letter processing.
- Exactly-once event processing semantics.

## Deep Docs

- [Event module](../../concepts/events.md)
- [Handler filtering](../../concepts/handler-filtering.md)
- [Polymorphic dispatch](../../concepts/polymorphic-dispatch.md)
