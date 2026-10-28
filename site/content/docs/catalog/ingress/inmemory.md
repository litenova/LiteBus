# In-Memory Inbox Ingress

- **ID**: `ingress.inmemory`
- **Name**: In-memory inbox ingress
- **Maturity**: GA
- **Summary**: This adapter consumes in-process channel deliveries and accepts them into the inbox through shared transport ingress. It is for testing and single-process development.

## Purpose and Scope

`UseInMemoryIngress` registers `InMemoryInboxIngressModule`. The module maps in-memory transport destinations into `TransportInboxIngressOptions` and registers the shared handler and consumer. It runs the same accept path as broker ingress without Docker. The enforced `Safety.MaxInFlightMessages` bound is 1.

This adapter holds the **reference matrix** for cross-broker ingress behavior (idempotency, headers, and requeue enabled and disabled), which broker Docker suites intentionally omit or cover only partially.

## Public Surface

```csharp
bus.AddInMemoryTransport();
bus.AddInbox(inbox => inbox.UseInMemoryIngress(ingress =>
{
    ingress.UseOptions(new InMemoryInboxIngressOptions
    {
        Destination = "commands.inbox",
        RequeueOnFailure = true
    });
}));
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseInMemoryIngress(Action<InMemoryInboxIngressModuleBuilder>)` | Registration extension |
| `InMemoryInboxIngressModuleBuilder.UseOptions(InMemoryInboxIngressOptions)` | Destination channel settings |
| `InMemoryInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without consumer loop |
| `InMemoryInboxIngressModule` | Child module |

## InMemory Options and Parity vs AMQP

| Capability | InMemory ingress | AMQP ingress |
| --- | --- | --- |
| Destination required | `Destination` required | `QueueName` required |
| Root transport required | `AddInMemoryTransport()` | `AddAmqpTransport(...)` |
| Native receive control | none | `PrefetchCount` |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| Shared `Safety` settings | yes | yes |
| Declare destination knobs | no | yes |

## Packages

- `LiteBus.Inbox.Ingress.InMemory`
- `LiteBus.Transport.InMemory` (explicit root transport)

## Requires

- `ingress.registration`
- Shared ingress capabilities
- `transport.inmemory`
- Inbox storage and contracts

## Invariants

- Delivery is process-local only, never cross-process.
- The adapter uses the same ack and requeue policy as broker ingress through `TransportInboxIngressConsumer`.
- Identity and idempotency defaults remain broker-scoped through shared mapper options.

## Non-Goals

- Production multi-node intake.
- Broker-specific semantics (TTL, DLQ, federation).

## Observability

Shared ingress metrics and logs apply unchanged:

| Signal | Meaning |
| --- | --- |
| `ingress.ack_failed_after_accept` | Ack failed after inbox accept |
| EventId 3002 | Loop restart after transport failure |
| EventId 3003 | Batch timer flush failure |
| EventId 3004 | Ack failed after accept |

Register the ingress meter with `AddLiteBusInboxMetrics()`.

## Identity, Idempotency, and Trusted Headers

InMemory ingress uses the same mapping defaults as broker adapters:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | A missing message id fails closed |
| `Safety.TrustApplicationHeaders` | false | App idempotency, tenant, and visibility headers are ignored |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | A duplicate redelivery maps to the same deduplication key |

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `UseInMemoryIngress_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `PublishThroughInMemoryTransport_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `DuplicateMessageId_ShouldCreateSingleInboxRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `UnknownContract_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InvalidJson_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `StoreFull_ShouldNotIncreasePendingRowsBeyondCapacity` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `MissingContractName_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `MissingContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `WrongContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InvalidMessageId_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `WrongClrShape_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- `DisableIngressConsumer` with in-memory ingress (covered for AMQP in PostgreSQL registration tests only).
- Cross-process in-memory delivery (process-local by design).
- Batch-accept path through the InMemory ingress builder (`Safety.EnableBatchAccept`) in integration suites. Every adapter exposes the option through the shared `Safety` record.

### Out-of-Scope

- Durability across process restarts (storage axis provides envelope durability after accept).
- Production multi-node intake.
- Simulating broker-specific edge cases (TTL, DLQ, federation).

## References

- [Inbox](../../reliable-messaging/inbox.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Integration tests](../../testing/integration-tests.md)
