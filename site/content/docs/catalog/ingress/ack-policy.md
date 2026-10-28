# Requeue and Discard Policy

- **ID**: `ingress.ack-policy`
- **Name**: Requeue and Discard Policy
- **Maturity**: GA
- **Summary**: The ack policy decides whether a failed delivery returns to the broker queue or is discarded after inbox accept errors.

## Purpose and Scope

`IngressAckPolicy` is the shared decision point used by `TransportInboxIngressConsumer` after a handler or store failure. It decides whether the consumer calls `ReturnToQueueAsync` or `DiscardAsync`.

This page covers only how the policy classifies failures. Broker-specific dead-letter routing is handled outside ingress.

## Public Surface

| API | Role |
| --- | --- |
| `IngressAckPolicy.ShouldRequeue(Exception, bool requeueOnFailure)` | Classifies a failure as requeue or discard |
| `IngressAckPolicy.UnwrapException(Exception)` | Unwraps reflection wrappers before classification |
| `TransportInboxIngressOptions.RequeueOnFailure` | Broker-level toggle that discards all failures when false |

## Classification Rules

The policy applies these checks in order.

### Global Switch

When `RequeueOnFailure` is `false`, the consumer discards on every failure path.

### Outer Exception Unwrapping

`IngressAckPolicy.UnwrapException` removes:

- `TargetInvocationException`
- `AggregateException` with one inner exception

This keeps policy behavior stable when reflection invokes the handlers.

### Terminal Exception Families

When requeue is enabled, the policy still treats these types as poison or configuration errors, and the consumer discards them:

- `MessageContractNotRegisteredException`
- `InboxDispatchException`
- `InboxIngressException`
- `InboxStorageException`
- `MessageSerializationException`
- `InvalidOperationException`
- `ArgumentException`
- `FormatException`
- `JsonException`
- `OperationCanceledException`

The policy walks the inner-exception chain, so a malformed payload wrapped in `MessageSerializationException` discards instead of requeueing forever. Any other exception type requeues when `RequeueOnFailure` is `true`.

Cancellation is never requeued. When the ingress token is cancelled, the consumer rethrows `OperationCanceledException` before settlement, so the delivery stays unacknowledged and the broker redelivers it. The offset is not committed without a store write.

## Packages

- `LiteBus.Inbox.Ingress`

## Requires

- `ingress.transport-consumer`
- Transport message `ReturnToQueueAsync` and `DiscardAsync` implementations per broker

## Invariants

- Successful accept always attempts ack. Ack failure is handled separately, with requeue for idempotent retry.
- Authorization callback failures follow the same policy as store failures.
- With `RequeueOnFailure = false`, poison messages drain instead of looping through infinite requeues.
- A cancelled acceptance is never settled. The consumer rethrows before ack, requeue, or discard.

## Broker Behavior Notes

The policy itself is broker-neutral. Each broker adapter maps `ReturnToQueueAsync` and `DiscardAsync` to its own native operations:

| Broker | Requeue path | Discard path |
| --- | --- | --- |
| AMQP | Nack with requeue | Nack or reject without requeue |
| Kafka | Offset left uncommitted, so the broker redelivers it | Offset committed without store write |
| AWS SQS | Message returned through visibility timeout or the explicit retry path | Message deleted without store write |
| Azure Service Bus | Message abandoned for redelivery | Message dead-lettered through the transport adapter |
| InMemory | Re-enqueued in the channel | Dropped from the in-memory queue |

## Non-Goals

- Dead-letter queue configuration (broker and ops concern).
- Inbox processor retry policy (processor axis).
- Custom exception taxonomy plug-in at runtime.

## Observability

The policy has no metrics or activities of its own. Its outcomes are visible indirectly:

| Outcome | Signal |
| --- | --- |
| Discard (no requeue) | Broker message removed or nacked without requeue, with no inbox row for poison paths |
| Requeue | `ReturnToQueueAsync` on transport message, followed by broker redelivery |
| Ack after success | `AcceptAsync` on transport message |

Consumer logs and broker telemetry reflect discard versus requeue. `ingress.ack_failed_after_accept` applies only when accept succeeded but ack failed, which is a separate path from policy classification.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `ShouldRequeue_WhenRequeueOnFailureTrueAndDiscardException_ShouldReturnFalse` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ShouldRequeue_WhenRequeueOnFailureTrueAndTransientIOException_ShouldReturnTrue` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ShouldRequeue_WhenRequeueOnFailureFalse_ShouldReturnFalseForTransientFailure` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenStorageFull_ShouldDiscardWithoutRequeue` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenAuthorizationRejects_ShouldDiscardWithoutAccept` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenAcceptCancelled_ShouldPropagateWithoutSettling` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `AcceptBatchAsync_WhenBatchAcceptCancelled_ShouldReleaseAdmissionAndRethrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `UnknownContract_ShouldNackWithoutRequeueAndSkipStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `InvalidJson_ShouldNackWithoutRequeueAndSkipStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `UnknownContract_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Ingress/AzureServiceBus/`, `Ingress/AwsSqs/`) |
| `InvalidJson_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Ingress/AzureServiceBus/`, `Ingress/AwsSqs/`) |
| `UnknownContract_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InvalidJson_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `UnknownContract_ShouldNackWithoutRequeueAndSkipPostgreSqlStore` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `InvalidJson_ShouldNackWithoutRequeueAndSkipPostgreSqlStore` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`, `Ingress/AzureServiceBus/`, `Ingress/AwsSqs/`, `Ingress/InMemory/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDrainQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`, `Ingress/InMemory/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |

### Untested

- Unwrapping of `TargetInvocationException` and single-inner `AggregateException` in integration scenarios.
- Every non-requeue exception type in the policy (`InboxDispatchException`, `InboxStorageException`, and similar), exercised on a live broker.
- Explicit requeue-off drain behavior on Kafka.
- Explicit requeue-off drain behavior on Azure Service Bus.
- Explicit requeue-off poison drain behavior on AMQP (called out in the ingress matrix).

### Out-of-Scope

- Dead-letter queue configuration (broker and ops concern).
- Inbox processor retry policy (processor axis).
- Custom exception taxonomy plug-in at runtime.

## References

- [AMQP transport: Guarantees](../../integrations/amqp.md)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
