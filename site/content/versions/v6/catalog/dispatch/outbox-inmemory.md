# Outbox InMemory Transport Dispatch

## Header

- **ID**: `dispatch.outbox.inmemory`
- **Name**: Outbox InMemory transport dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes through in-process channels for tests and local pipelines without external brokers.

## What It Does

`AddInMemoryTransport()` registers `InMemoryTransportModule` at the root. `UseInMemoryDispatch` on `OutboxModuleBuilder` registers shared transport outbox dispatch as a feature bridge. This simulates broker publication for integration tests and sample multi-module hosts running in one process.

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

- Default hook failure policy: `CompleteDespiteHookFailure`.
- Same at-least-once processor semantics as broker outbox dispatch.
- Destination name must align with consumer/InMemory ingress configuration in tests.

## Non-Goals

- Not a production message bus.
- Does not provide cross-process delivery.

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
| `OutboxModuleBuilder.UseInMemoryDispatch(Action<TransportOutboxDispatcherOptions>? configure = null)` | Registers outbox transport dispatcher that requires the root in-memory transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | Defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Shared outbox publish flow over in-process channel transport |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | In-memory publisher span per simulated broker send |
| `litebus.outbox.processor.published` / `failed` | Terminal store outcomes |
| `litebus.outbox.processor.persist_skipped` | Relevant when testing at-least-once republication scenarios |

`InMemoryOutboxAtLeastOnceIntegrationTests` documents crash-between-publish-and-persist behavior at the processor layer.

## Deep Docs

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

- Hook failure path with `CompleteDespiteHookFailure`.
- Tenant routing strategy and route resolver overrides.
- Cross-process outbox to inbox topology (adapter is in-process only).

### Out-of-Scope

- Production message bus role
- Cross-process delivery
