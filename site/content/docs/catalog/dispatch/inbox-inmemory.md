# Inbox InMemory Transport Dispatch

## Header

- **ID**: `dispatch.inbox.inmemory`
- **Name**: Inbox InMemory transport dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes through in-process channels for tests and local multi-service pipelines without external brokers.

## What It Does

`AddInMemoryTransport()` registers `InMemoryTransportModule` at the root, and `UseInMemoryDispatch` registers `TransportInboxDispatchModule` as its inbox feature bridge. Messages flow through `System.Threading.Channels` to a logical destination name. Integration tests use this path to simulate remote dispatch and pair it with InMemory ingress.

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

- It is thread-safe within one process, but it does not work across processes.
- When a consume fails, the requeue re-enqueues the message on the channel. The transport invoker owns that behavior.
- It uses the same header mapping as the broker transports.

## Non-Goals

- It is not for production cross-host messaging.
- It does not survive process restarts.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddInMemoryTransport();

    litebus.AddInbox(inbox =>
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
| `InboxModuleBuilder.UseInMemoryDispatch(Action<TransportInboxDispatcherOptions>? configure = null)` | Registers the inbox transport dispatcher, which requires the root in-memory transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | The shared publish logic over the in-process channel transport |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | The `InMemoryPublisher` emits it with destination and route tags |
| `process {destination}` | The paired ingress or consumer in the same process |
| `litebus.transport.circuit_breaker.*` | Tag `inmemory`. The InMemory transport registers a standalone breaker |
| `litebus.inbox.processor.*` | The standard processor metrics |

The activities remain available for test assertions even without external OTLP export.

## References

- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |
| `ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |

### Untested

- The inbox dispatch failure and retry path beyond the persist-skipped scenario.
- The custom route resolver and tenant route strategy branches.
- Cross-process delivery, which the in-memory adapter does not support.

### Out-of-Scope

- Production cross-host messaging
- Surviving process restarts
