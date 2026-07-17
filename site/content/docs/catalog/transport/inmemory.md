# In-Memory Transport

- **ID**: `transport.inmemory`
- **Name**: In-memory transport
- **Maturity**: GA
- **Summary**: In-process channel broker used for fast tests and local workflows while preserving transport contracts.

## What It Does

`InMemoryTransportModule` registers `InMemoryTransportBroker`, `InMemoryPublisher`, and `InMemoryConsumer`. Messages are queued by destination inside process memory and delivered through the same `TransportMessage` contract used by external brokers.

Ack behavior is explicit. `AcceptAsync` is a no-op, `DiscardAsync` drops delivery, and `ReturnToQueueAsync` re-enqueues with `Redelivered = true`.

## Public Surface

| API | Role |
| --- | --- |
| `InMemoryTransportModule` | Registers in-memory transport services |
| `InMemoryTransportBroker` | Queue registry keyed by destination |
| `InMemoryPublisher.PublishAsync` | Enqueue publish request body and headers |
| `InMemoryConsumer.StartAsync` | Dequeue and invoke handler loop |
| `InMemoryDestinationEndpoint` | Destination queue and synchronization |
| `InMemoryPendingDelivery` | Delivery snapshot with redelivery bit |
| `LiteBusInMemoryTelemetry.MeterName` | Reserved in-memory adapter meter |

## Packages

- `LiteBus.Transport.InMemory`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`

## Invariants

- Transport is process-local and not durable.
- Redelivery requires explicit `ReturnToQueueAsync` or exception path through invoker behavior.
- Shared transport metrics are tagged `inmemory`.

## Non-Goals

- Cross-process messaging.
- Durability and replay across restart.
- Replacing external brokers for production workloads.

## Observability

### Metrics

| Item | Value |
| --- | --- |
| Shared meter | `LiteBus.Transport` |
| Broker tag | `litebus.transport.broker="inmemory"` |
| Shared gauges | `litebus.transport.circuit_breaker.open`, `litebus.transport.circuit_breaker.failure_count` |
| Reserved adapter meter | `LiteBus.Transport.InMemory` |
| OpenTelemetry registration | `AddLiteBusTransportMetrics()` |

### Tracing

- Activity source `LiteBus.Transport`
- Spans `send {destination}` and `process {destination}` with `messaging.system=litebus_in_memory`

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishAndConsume_ShouldDeliverMessageToConsumer` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `ReturnToQueue_ShouldRedeliverMessage` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `HandlerThrow_ShouldRequeueMessageByDefault` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `PublishThroughInMemoryTransport_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |

### Untested

- Gauge export assertions with broker tag `inmemory`.
- Circuit-breaker-open behavior in a forced-failure in-memory path.

### Out-of-Scope

- Cross-process transport behavior.
- Persistence and restart recovery.

## Deep Docs

- [Architecture.md](../../architecture/README.md)
- [Integration-Tests.md](../../testing/integration-tests.md)
