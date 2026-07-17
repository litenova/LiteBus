# Outbox In-Process Event Dispatch

## Header

- **ID**: `dispatch.outbox.in-process`
- **Name**: Outbox in-process event dispatch
- **Maturity**: GA
- **Summary**: Deserialize leased outbox envelopes and publish them through `IEventMediator.PublishAsync` in the same process.

## What It Does

`EventOutboxDispatcher` implements `IOutboxDispatcher`. It resolves the contract, unprotects and deserializes the payload, builds `EventMediationSettings` with trace metadata, and publishes via `IEventMediator`. Types implementing `IEvent` use the non-generic publish overload; POCO events use a cached closed-generic delegate per event type.

Use this path when integration events should fan out to local handlers only (same service or same process) while still benefiting from durable enqueue and at-least-once processor semantics.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.InProcess` | Dispatcher and registration |
| `LiteBus.Events.Abstractions` | `IEvent`, `IEventMediator` |
| `LiteBus.Outbox.Abstractions` | `IOutboxDispatcher`, envelopes |

## Requires

- `durable-core.outbox`
- `mediator.events` (`AddEventModule`)
- `runtime.contract-registry`
- `dispatch.registration`

## Invariants

- Event types must be registered with `Contracts.Register` and have handlers when using POCO events.
- Dispatch wraps non-`LiteBusDispatchException` failures in `LiteBusDispatchException` with contract context.
- Default hook failure policy is `DeadLetter` (stricter than transport outbox dispatch).
- At-least-once local publication: handlers must tolerate duplicate event delivery.

## Non-Goals

- Does not publish to external brokers.
- Does not guarantee cross-process delivery.
- Does not invoke command handlers.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddEventModule(events => { /* handlers */ });
    litebus.AddOutboxModule(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseInProcessDispatch();
    });
});
```

### `OutboxModuleBuilder.UseInProcessDispatch()`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Registers | `EventOutboxDispatchModule` to `EventOutboxDispatcher` |

### `EventOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Flow | Resolve contract to unprotect to deserialize to build `EventMediationSettings` to publish |
| `IEvent` path | `IEventMediator.PublishAsync(IEvent, settings, token)` |
| POCO path | Cached closed-generic `PublishAsync<TEvent>` delegate per contract type |
| Errors | Rethrows `LiteBusDispatchException`; wraps other exceptions with contract context |

Optional constructor dependency: `IOutboxPayloadProtector`.

### `EventOutboxDispatchModule.Build(IModuleConfiguration)`

Registers `IOutboxDispatcher` to `EventOutboxDispatcher`. Default hook failure policy remains processor default (`DeadLetter`), not transport outbox `CompleteDespiteHookFailure`.

## Observability

| Signal | When |
| --- | --- |
| `litebus.outbox.processor.dispatch_duration` | Processor histogram around `DispatchAsync` |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | Terminal outcomes after dispatch and hooks |
| Event pipeline tracing | Follows mediator/event handler instrumentation when configured; no outbox-dispatch-specific activity source |
| No transport send span | In-process path does not touch the transport layer |

Register outbox processor metrics through `AddLiteBusOutboxMetrics()`.

## Analyzers

- **LB1007** / **LB1017**: Contract registration for published event types.
- **LB1014**: Processor without dispatcher.

## Deep Docs

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Event-Module.md](../../concepts/events.md)
- [Domain-Events-and-Unit-of-Work.md](../../concepts/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `InProcessOutboxDispatcher_ShouldPublishPocoEvent` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `ProcessPendingAsync_ShouldPublishEventThroughPostgreSqlStore` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `ProcessPendingAsync_WhenDispatcherFails_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_published` | `LiteBus.Outbox.UnitTests` |
| `AddOutboxInProcessDispatcher_ShouldRegisterInProcessOutboxDispatcher` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |

### Untested

- Explicit assertion for non-LiteBus failure wrapping into `LiteBusDispatchException`.
- EF Core outbox adapter end-to-end coverage in dispatch suites.

### Out-of-Scope

- Publishing to external brokers
- Cross-process event delivery guarantees
- Invoking command handlers from outbox dispatch
