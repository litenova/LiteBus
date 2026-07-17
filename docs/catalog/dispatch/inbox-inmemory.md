# Inbox InMemory Transport Dispatch

## Header

- **ID**: `dispatch.inbox.inmemory`
- **Name**: Inbox InMemory transport dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes through in-process channels for tests and local multi-service pipelines without external brokers.

## What It Does

`UseInMemoryDispatch` registers `TransportInboxDispatchModule` with `InMemoryTransportModule`. Messages flow through `System.Threading.Channels` to a logical destination name. Used heavily in integration tests to simulate remote dispatch and pair with InMemory ingress on another module instance.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InMemory` | Registration glue |
| `LiteBus.Inbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.InMemory` | Channel-based transport |

## Requires

- `dispatch.transport-core`
- `transport.inmemory`
- `durable-core.inbox`

## Invariants

- Thread-safe within one process; not cross-process.
- Requeue on failed consume re-enqueues on the channel (transport invoker).
- Same header mapping as broker transports.

## Non-Goals

- Not for production cross-host messaging.
- Does not survive process restarts.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddInboxModule(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseInMemoryDispatch(options =>
        {
            options.DefaultDestination = "orders.commands";
        });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseInMemoryDispatch(Action<TransportInboxDispatcherOptions>? configure = null)` | Registers inbox transport dispatcher with in-memory transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Shared publish logic over in-process channel transport |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Emitted by `InMemoryPublisher` with destination and route tags |
| `process {destination}` | Paired ingress/consumer in the same process |
| `litebus.transport.circuit_breaker.*` | Tag `inmemory`; standalone breaker registered by InMemory transport |
| `litebus.inbox.processor.*` | Standard processor metrics |

Activities remain available for test assertions even without external OTLP export.

## Deep Docs

- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |
| `ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |

### Untested

- Inbox dispatch failure and retry path beyond persist-skipped scenario.
- Custom route resolver and tenant route strategy branches.
- Cross-process delivery, which the in-memory adapter does not support.

### Out-of-Scope

- Production cross-host messaging
- Surviving process restarts
