# Outbox In-Process Event Dispatch

## Header

- **ID**: `dispatch.outbox.in-process`
- **Name**: Outbox in-process event dispatch
- **Maturity**: GA
- **Summary**: Deserialize leased outbox envelopes and publish them through `IEventMediator.PublishAsync` in the same process.

## What It Does

`EventOutboxDispatcher` implements `IOutboxDispatcher`. It resolves the contract, unprotects and deserializes the payload, builds `EventMediationSettings` with trace metadata, and publishes through `IEventMediator`. Types that implement `IEvent` use the non-generic publish overload. POCO events use a cached closed-generic delegate per event type.

Use this path when integration events should fan out to local handlers only, in the same service or process, while keeping durable enqueue and at-least-once processor semantics.

A dispatched message with no registered handler fails the attempt by default (`RequireHandler = true`). The failure retries the message and eventually dead-letters it. Set `RequireHandler = false` only when publishing to zero handlers is deliberate. An envelope that records one handler through per-handler delivery publishes to that handler alone. Pipeline observers (pre-stage, post, error, and completion stages) still run on every targeted dispatch.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.InProcess` | Dispatcher and registration |
| `LiteBus.Events.Abstractions` | `IEvent`, `IEventMediator` |
| `LiteBus.Outbox.Abstractions` | `IOutboxDispatcher`, envelopes |

## Requires

- `durable-core.outbox`
- `mediator.events` (`AddEvents`)
- `runtime.contract-registry`
- `dispatch.registration`

## Invariants

- Dispatched event types must be registered with `Contracts.Register`. With the default `RequireHandler = true`, dispatch fails when no handler is registered for the event.
- Dispatch rethrows `OperationCanceledException` when the caller token is cancelled instead of wrapping it. It wraps other non-`LiteBusDispatchException` failures in `LiteBusDispatchException` with contract context. It adds registration guidance only for `MessageContractNotRegisteredException` and `NoHandlerFoundException`.
- The default hook failure policy is `DeadLetter`, which is stricter than transport outbox dispatch.
- Local publication is at-least-once, so handlers must tolerate duplicate event delivery.
- Per-handler envelopes match main handlers by full type name through an `EventRoutingSettings.HandlerPredicate`. When no subscribed handler matches the recorded name, the predicate matches nothing, and `RequireHandler` decides whether the attempt fails or completes as published.

## Non-Goals

- It does not publish to external brokers.
- It does not guarantee cross-process delivery.
- It does not invoke command handlers.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddEvents(events => { /* handlers */ });
    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseInProcessDispatch();
    });
});
```

### `OutboxModuleBuilder.UseInProcessDispatch(Action<InProcessOutboxDispatchOptionsBuilder>? configure = null)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Registers | `EventOutboxDispatchModule` to `EventOutboxDispatcher` |

```csharp
outbox.UseInProcessDispatch(dispatch => dispatch.RequireHandler = false);
```

| Option | Default | Role |
| --- | --- | --- |
| `InProcessOutboxDispatchOptions.RequireHandler` | `true` | Fail the attempt when no handler is registered. `false` completes the dispatch and marks the message published without any handler having run |

### `EventOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Flow | Resolves the contract, unprotects and deserializes the payload, builds `EventMediationSettings`, and publishes |
| `IEvent` path | `IEventMediator.PublishAsync(IEvent, settings, token)` |
| POCO path | A cached closed-generic `PublishAsync<TEvent>` delegate per contract type |
| Per-handler path | `EventRoutingSettings.HandlerPredicate` restricts main handlers to the recorded handler full name. An envelope without a recorded handler publishes to every subscribed handler |
| Errors | Rethrows `LiteBusDispatchException` and caller cancellation. Wraps other exceptions with contract context, with registration guidance only for `MessageContractNotRegisteredException` and `NoHandlerFoundException` |

The optional constructor dependency is `IOutboxPayloadProtector`.

### `EventOutboxDispatchModule.Build(IModuleConfiguration)`

The method registers `IOutboxDispatcher` to `EventOutboxDispatcher`. The default hook failure policy remains the processor default (`DeadLetter`), not the transport outbox `CompleteDespiteHookFailure`.

## Observability

| Signal | When |
| --- | --- |
| `litebus.outbox.processor.dispatch_duration` | A processor histogram around `DispatchAsync` |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | The terminal outcomes after dispatch and hooks |
| Event pipeline tracing | It follows mediator and event handler instrumentation when configured. There is no outbox-dispatch-specific activity source |
| No transport send span | The in-process path does not touch the transport layer |

Register outbox processor metrics through `AddLiteBusOutboxMetrics()`.

## Analyzers

- **LB1007** / **LB1017**: Contract registration for published event types.
- **LB1014**: A processor without a dispatcher.

## References

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Event-Module.md](../../concepts/events.md)
- [Domain-Events-and-Unit-of-Work.md](../../reliable-messaging/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `InProcessOutboxDispatcher_ShouldPublishPocoEvent` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `A_message_with_no_handler_is_not_marked_published` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`, `InProcessOutboxDispatchRequireHandlerTests`) |
| `Turning_RequireHandler_off_restores_the_silent_no_op` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`, `InProcessOutboxDispatchRequireHandlerTests`) |
| `EnqueueAsync_should_write_one_envelope_per_subscribed_handler` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `Drain_should_retry_only_the_failing_reaction` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `DeadLetter_and_requeue_should_apply_per_handler` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `EnqueueAsync_without_a_subscribed_handler_should_throw` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `EnqueueAsync_should_derive_idempotency_keys_per_handler` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `EnqueueAsync_should_replay_supplied_identity_to_the_same_per_handler_rows` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `Targeted_dispatch_should_run_observers_on_every_envelope` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `EnqueueBatchAsync_should_flatten_per_handler_envelopes` | `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`) |
| `ProcessPendingAsync_ShouldPublishEventThroughPostgreSqlStore` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `ProcessPendingAsync_WhenDispatcherFails_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_published` | `LiteBus.Outbox.UnitTests` |
| `AddOutboxInProcessDispatcher_ShouldRegisterInProcessOutboxDispatcher` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |

### Untested

- An explicit assertion for non-LiteBus failure wrapping into `LiteBusDispatchException`.
- EF Core outbox adapter end-to-end coverage in the dispatch suites.

### Out-of-Scope

- Publishing to external brokers
- Cross-process event delivery guarantees
- Invoking command handlers from outbox dispatch
