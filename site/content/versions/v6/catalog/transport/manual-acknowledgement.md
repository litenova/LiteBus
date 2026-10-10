# Manual Acknowledgement Model

- **ID**: `transport.manual-acknowledgement`
- **Name**: Manual acknowledgement model
- **Maturity**: GA
- **Summary**: Every inbound delivery exposes explicit accept, discard, and requeue operations mapped to broker-specific ack semantics.

## What It Does

`TransportMessage` wraps broker acknowledgement as three user-facing methods. `AcceptAsync` signals successful processing. `DiscardAsync` rejects without requeue (delete, complete without retry, or dead-letter depending on broker). `ReturnToQueueAsync` rejects with requeue so the broker redelivers the message. Adapters supply `AckAsync` and `NackAsync(requeue)` delegates through `TransportConsumerAckHandlers`.

Each adapter maps these calls to its native API. The Architecture resilience matrix documents the minimum behavior per provider.

| Provider | Accept | Requeue | Notes |
| --- | --- | --- | --- |
| AMQP | `basic.ack` | `basic.nack` requeue | Channel shutdown stops consumer |
| InMemory | Remove from channel | Re-enqueue | In-process only |
| Azure Service Bus | `CompleteMessage` | `AbandonMessage` | Exponential processor restart on errors |
| AWS SQS | `DeleteMessage` | `ChangeMessageVisibility` with backoff | Uses receive count when present |
| Kafka | Offset commit | Seek to failed offset | No queue-style lease; offset not advanced until accept |

## Public Surface

| API | Role |
| --- | --- |
| `TransportMessage.AcceptAsync(CancellationToken)` | Acknowledge successful processing |
| `TransportMessage.DiscardAsync(CancellationToken)` | Reject without requeue |
| `TransportMessage.ReturnToQueueAsync(CancellationToken)` | Reject with requeue |
| `TransportMessage.Redelivered` | Broker redelivery hint |
| `TransportMessage.AckAsync` / `NackAsync` | Low-level delegates used by mappers |
| `TransportConsumerAckHandlers` | Factory for ack delegates on mapped messages |

## Packages

- `LiteBus.Transport.Abstractions`
- Broker adapters in `LiteBus.Transport.*`

## Requires

- `transport.publish-consume-contracts`

## Invariants

- Handlers that return without calling an ack method leave deliveries unacknowledged; broker behavior varies (peek-lock renewal, visibility timeout, or uncommitted Kafka offset).
- Raw `IMessageConsumer` hosts have no LiteBus redelivery cap at the transport layer; poison handling requires `DiscardAsync`, broker DLQ policy, or inbox ingress.
- Kafka commits offset only after `AcceptAsync`; `ReturnToQueueAsync` seeks back before the next read.

## Non-Goals

- Store-backed retry budgets, dead-letter tables, or idempotency enforcement (inbox processor and ingress own those).
- Exactly-once delivery at the broker.
- Automatic ack on successful handler return (explicit ack is required).

## Observability

- `TransportMessage.Redelivered` surfaces on Azure Service Bus when `DeliveryCount > 1`.
- Process spans record destination, message id, and redelivery state when tracing is enabled.
- No dedicated ack success/failure counters at the transport layer.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishAsync_ThenConsume_AcknowledgesMessage` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ConsumeAsync_NackWithRequeue_RedeliversMessage` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ReturnToQueue_ShouldRedeliverMessage` | `LiteBus.Transport.UnitTests` |
| `ToTransportMessage_ShouldExposeCommitDelegate` | `LiteBus.Transport.UnitTests` |
| `ToTransportMessage_ReturnToQueueAsync_ShouldSeekToConsumedOffset` | `LiteBus.Transport.UnitTests` |
| `ToTransportMessage_DiscardAsync_ShouldNotSeek` | `LiteBus.Transport.UnitTests` |
| `ToTransportMessage_ShouldExposeAckDelegates` | `LiteBus.Transport.UnitTests` |
| `ComputeRequeueVisibilityTimeout_shouldHonorReceiveCount` | `LiteBus.Transport.UnitTests` |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Amqp/`, `Ingress/AwsSqs/`, `Ingress/AzureServiceBus/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDrainQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `TransientAcceptFailure_ShouldRedeliverSameOffsetWithoutRestart` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |

### Untested

- Azure Service Bus `DiscardAsync` dead-letter path when DLQ is configured.
- SQS ack failure after successful handler completion at raw consumer layer.
- AMQP ack when channel closes mid-handler.

### Out-of-Scope

- Store-backed retry budgets, dead-letter tables, or idempotency enforcement.
- Exactly-once delivery at the broker.
- Automatic ack on successful handler return.

## Deep Docs

- [Architecture.md](../../architecture/README.md) (Transport resilience capability matrix, Poison message handling)
- [Kafka-Transport.md](../../integrations/kafka.md) (offset and seek semantics)
