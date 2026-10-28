# Batch Inbox Accept Buffering

- **ID**: `ingress.batch-accept`
- **Name**: Batch inbox accept buffering
- **Maturity**: GA
- **Summary**: This capability buffers transport deliveries for `IInbox.AcceptBatchAsync` while settling each broker delivery separately.

## Purpose and Scope

When `TransportInboxIngressSafetyOptions.EnableBatchAccept` is true, `TransportInboxIngressConsumer` buffers `BatchSize` deliveries. It flushes when the buffer reaches that size, when `BatchMaxWait` elapses after the first buffered delivery, or when the consumer stops.

`BatchSize` is independent of native broker controls. For example, AMQP can prefetch 50 unacknowledged deliveries while each inbox store call accepts 10 rows:

```csharp
ingress.UseOptions(new AmqpInboxIngressOptions
{
    QueueName = "orders.commands",
    PrefetchCount = 50,
    Safety = new TransportInboxIngressSafetyOptions
    {
        EnableBatchAccept = true,
        BatchSize = 10,
        BatchMaxWait = TimeSpan.FromMilliseconds(200)
    }
});
```

On batch accept failure, the consumer falls back to per-message accept so one poison delivery does not reject unrelated messages. After a successful store call, each broker delivery is acknowledged separately.

A semaphore bounded by `BatchSize` prevents another buffer from growing while the current batch flushes. Cancellation skips the fallback. A cancelled batch accept releases admission for the whole batch and rethrows, leaving buffered deliveries unacknowledged.

## Flush and Fallback Flow

```mermaid
flowchart TD
  M[Delivery arrives] --> B[Add to bounded buffer]
  B --> T{BatchSize reached?}
  T -- yes --> F[Flush via AcceptBatchAsync]
  T -- no --> W{BatchMaxWait elapsed?}
  W -- yes --> F
  W -- no --> K[Keep buffering]
  F --> S{Store accepted batch?}
  S -- yes --> A[Acknowledge each delivery]
  S -- no --> P[Fall back to per-message accept]
  P --> A
```

## Public Surface

| Property | Default | Role |
| --- | --- | --- |
| `Safety.EnableBatchAccept` | false | Turn on delivery buffering |
| `Safety.BatchSize` | 10 | Set buffer capacity and the store flush threshold |
| `Safety.BatchMaxWait` | 200 ms | Flush a partial batch under low traffic |
| `Safety.MaxInFlightMessages` | 32 | Limit concurrent LiteBus handler work outside the batch buffer |

Every ingress adapter exposes the same `Safety` record. AMQP prefetch, SQS receive size, and Azure callback concurrency do not change the inbox batch threshold.

## Packages

- `LiteBus.Inbox.Ingress`
- The selected `LiteBus.Inbox.Ingress.*` adapter

## Requires

- `ingress.transport-consumer`
- `ingress.transport-handler`
- A store that implements `IInbox.AcceptBatchAsync`

## Invariants

- Shutdown flushes a partial buffer before the transport consumer stops.
- After a successful batch store, the broker acknowledgement stays per delivery.
- After a failed batch store call, the consumer falls back to per-message acceptance.
- A cancelled batch accept releases admission for the whole batch and rethrows without per-message fallback.
- `BatchSize` must be greater than zero. An invalid value fails at module composition. `BatchMaxWait` of zero with batch accept enabled fails the same way, because a zero wait never flushes partial batches.

## Observability

| Signal | When emitted |
| --- | --- |
| EventId 3003, Error, `BatchFlushFailed` | A timed partial-batch flush throws |
| EventId 3006, Warning, `BatchAcceptFallback` | Batch accept fails and per-message fallback begins |
| `ingress.ack_failed_after_accept` | One broker acknowledgement fails after store acceptance |

## Test Coverage

| Test method | Project |
| --- | --- |
| `BatchAccept_WhenBufferFull_ShouldBlockUntilFlushCompletes` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `BatchAccept_WhenOneDeliveryFails_ShouldAcknowledgeSuccessfulDeliveriesOnly` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `BatchAccept_ShouldFlushPartialBatchAfterBatchMaxWait` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `AcceptBatchAsync_WhenBatchAcceptCancelled_ShouldReleaseAdmissionAndRethrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `EnableBatchAccept_AtBatchSize_ShouldFlushAllMessages` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `EnableBatchAccept_BeforeBatchSize_ShouldFlushAfterBatchMaxWait` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |

Provider integration suites do not cover live Kafka, Azure Service Bus, or SQS batch acceptance. Their builders use the same tested safety record and shared consumer.

## References

- [Ingress safety options](safety-options.md)
- [Inbox](../../reliable-messaging/inbox.md)
