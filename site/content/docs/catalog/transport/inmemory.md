# In-Memory Transport

- **ID**: `transport.inmemory`
- **Name**: In-memory transport
- **Maturity**: GA
- **Summary**: In-process channel broker for fast tests and local workflows that keeps the transport contracts of external brokers.

## What It Does

`InMemoryTransportModule` registers `InMemoryTransportBroker`, `InMemoryPublisher`, and `InMemoryConsumer`. Messages are queued by destination in process memory and delivered through the same `TransportMessage` contract used by external brokers.

Each destination admits at most `InMemoryTransportOptions.DestinationCapacity` unsettled deliveries, including queued and currently handled messages. The default is 1024. When the limit is reached, `PublishAsync` waits asynchronously until a delivery settles or the caller cancels. A full destination waits for capacity with `BoundedChannelFullMode.Wait` ([.NET channels guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)). The transport does not drop old or new writes.

Ack behavior is explicit. `AcceptAsync` and `DiscardAsync` release the delivery's capacity reservation. `ReturnToQueueAsync` re-enqueues with `Redelivered = true` and keeps the reservation, so a waiting publisher cannot take the only slot and deadlock the redelivery. The consumer runs one handler at a time regardless of the requested `MaxInFlightMessages`.

`InMemoryTransportBroker` implements `IDisposable` and completes its destination channels on dispose. The consumer's `StopAsync` and `WaitUntilStoppedAsync` complete after the channel is closed. The publisher is a singleton and consumers are transient.

```csharp
bus.AddInMemoryTransport(new InMemoryTransportOptions
{
    DestinationCapacity = 256
});
```

## Public Surface

| API | Role |
| --- | --- |
| `InMemoryTransportOptions.DestinationCapacity` | Per-destination queued and in-flight bound |
| `InMemoryTransportModule` | Registers in-memory transport services |
| `InMemoryTransportBroker` | Queue registry keyed by destination |
| `InMemoryPublisher.PublishAsync` | Enqueue publish request body and headers |
| `InMemoryConsumer.StartAsync` | Dequeue and invoke handler loop |
| `InMemoryDestinationEndpoint` | Destination queue and synchronization (internal) |
| `InMemoryPendingDelivery` | Delivery snapshot with redelivery bit (internal) |
| `LiteBusInMemoryTelemetry.MeterName` | Reserved in-memory adapter meter |

## Packages

- `LiteBus.Transport.InMemory`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`

## Invariants

- The transport is process-local and not durable.
- Destination capacity is positive, applies independently to every destination, and is validated during composition.
- Full destinations wait for capacity. The transport does not drop old or new writes.
- Redelivery happens when the handler calls `ReturnToQueueAsync` explicitly, when the invoker requeues after an exception, or when a handler returns without settling.
- A handler that returns successfully without settling is returned to the destination with `Redelivered = true` instead of being dropped.
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
| `StartAsync_WithMaxInFlightEight_ShouldEnforceInMemoryBoundOfOne` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `Build_ShouldRegisterSingletonPublisherAndTransientConsumer` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `GetOrCreateEndpoint_AfterDispose_ShouldThrowObjectDisposedException` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `StopAsync_AfterBrokerDispose_ShouldCompleteWithoutThrowing` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `HandlerThrow_ShouldRequeueMessageByDefault` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `PublishAsync_WhenDestinationIsFull_ShouldWaitForSettlement` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `PublishAsync_WhenCapacityWaitIsCanceled_ShouldReleaseAdmissionWaiter` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `Constructor_WithNonPositiveDestinationCapacity_ShouldThrow` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `PublishThroughInMemoryTransport_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |
| `HandlerWithoutSettlement_ShouldRedeliverInsteadOfDrop` | `LiteBus.Transport.UnitTests` (`InMemory/`) |

### Untested

- Gauge export assertions with broker tag `inmemory`.
- Circuit-breaker-open behavior in a forced-failure in-memory path.

### Out-of-Scope

- Cross-process transport behavior.
- Persistence and restart recovery.

## References

- [Architecture.md](../../architecture/README.md)
- [Integration-Tests.md](../../testing/integration-tests.md)
