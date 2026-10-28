# Outbox InMemory Transport Dispatch

## Header

- **ID**: `dispatch.outbox.inmemory`
- **Name**: Outbox InMemory transport dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes through in-process channels for tests and local pipelines without external brokers.

## What It Does

`AddInMemoryTransport()` registers `InMemoryTransportModule` at the root. `UseInMemoryDispatch` on `OutboxModuleBuilder` registers the shared transport outbox dispatch as a feature bridge. This simulates broker publication for integration tests and for sample multi-module hosts that run in one process.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.InMemory` | Registration glue |
| `LiteBus.Outbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.InMemory` | Channel-based transport |

## Requires

- `dispatch.transport-core`
- `transport.inmemory`
- `durable-core.outbox`

## Invariants

- The default hook failure policy is `CompleteDespiteHookFailure`.
- It has the same at-least-once processor semantics as broker outbox dispatch.
- The destination name must align with the consumer or InMemory ingress configuration in tests.

## Non-Goals

- It is not a production message bus.
- It does not provide cross-process delivery.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddInMemoryTransport();

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseInMemoryDispatch(options =>
        {
            options.DefaultDestination = "orders.events";
        });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseInMemoryDispatch(Action<TransportOutboxDispatcherOptions>? configure = null)` | Registers the outbox transport dispatcher, which requires the root in-memory transport |
| `TransportOutboxDispatchModule<TTransportModule>.DefaultHookFailurePolicy` | The policy defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | The shared outbox publish flow over the in-process channel transport |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | An in-memory publisher span per simulated broker send |
| `litebus.outbox.processor.published` / `failed` | The terminal store outcomes |
| `litebus.outbox.processor.persist_skipped` | It supports tests of at-least-once republication scenarios |

`InMemoryOutboxAtLeastOnceIntegrationTests` documents the crash-between-publish-and-persist behavior at the processor layer.

## References

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |

### Untested

- The hook failure path with `CompleteDespiteHookFailure`.
- Tenant routing strategy and route resolver overrides.
- A cross-process outbox to inbox topology. The adapter is in-process only.

### Out-of-Scope

- Production message bus role
- Cross-process delivery
