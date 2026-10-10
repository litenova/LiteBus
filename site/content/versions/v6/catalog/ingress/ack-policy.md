# Requeue and Discard Policy

- **ID**: `ingress.ack-policy`
- **Name**: Requeue and Discard Policy
- **Maturity**: GA
- **Summary**: Decides whether a failed delivery returns to the broker queue or is discarded after inbox accept errors.

## Purpose and Scope

`IngressAckPolicy` is the shared decision point used by `TransportInboxIngressConsumer` after a handler or store failure. It decides if the consumer calls `ReturnToQueueAsync` or `DiscardAsync`.

This page covers classification semantics only. Broker-specific dead-letter routing is outside ingress.

## Public Surface

| API | Role |
| --- | --- |
| `IngressAckPolicy.ShouldRequeue(Exception, bool requeueOnFailure)` | Classify failure for requeue vs discard |
| `IngressAckPolicy.UnwrapException(Exception)` | Unwrap reflection wrappers before classification |
| `TransportInboxIngressOptions.RequeueOnFailure` | Broker-level toggle (when false, all failures discard) |

## Classification Rules

### Step 1: Apply Global Switch

When `RequeueOnFailure` is `false`, every failure path discards.

### Step 2: Unwrap Outer Exception Wrappers

`IngressAckPolicy.UnwrapException` removes:

- `TargetInvocationException`
- `AggregateException` with one inner exception

This keeps policy behavior stable when handlers are invoked through reflection boundaries.

### Step 3: Match Terminal (Non-Requeue) Exception Families

These types are always treated as poison or configuration errors and discard when requeue is enabled:

- `MessageContractNotRegisteredException`
- `InboxDispatchException`
- `InboxIngressException`
- `InboxStorageException`
- `InvalidOperationException`
- `ArgumentException`
- `FormatException`
- `JsonException`

Any other exception type requeues when `RequeueOnFailure` is `true`.

## Packages

- `LiteBus.Inbox.Ingress`

## Requires

- `ingress.transport-consumer`
- Transport message `ReturnToQueueAsync` / `DiscardAsync` implementations per broker

## Invariants

- Successful accept always attempts ack; ack failure is handled separately (requeue for idempotent retry).
- Policy applies to authorization callback failures the same as store failures.
- `RequeueOnFailure = false` drains poison messages instead of infinite requeue loops.

## Broker Behavior Notes

`IngressAckPolicy` itself is broker-neutral. Broker adapters differ in how `ReturnToQueueAsync` and `DiscardAsync` map to native semantics:

| Broker | Requeue path | Discard path |
| --- | --- | --- |
| AMQP | Nack with requeue | Nack or reject without requeue |
| Kafka | Offset left uncommitted and redelivered | Offset committed without store write |
| AWS SQS | Message returned by visibility timeout / explicit retry path | Message deleted without store write |
| Azure Service Bus | Message abandoned for redelivery | Message completed or dead-letter flow owned by transport setup |
| InMemory | Re-enqueue in channel | Drop from in-memory queue |

## Non-Goals

- Dead-letter queue configuration (broker and ops concern).
- Inbox processor retry policy (processor axis).
- Custom exception taxonomy plug-in at runtime.

## Observability

No ack-policy-specific metrics or activities. Outcomes are visible indirectly:

| Outcome | Signal |
| --- | --- |
| Discard (no requeue) | Broker message removed or nacked without requeue; no inbox row for poison paths |
| Requeue | `ReturnToQueueAsync` on transport message; broker redelivers |
| Ack after success | `AcceptAsync` on transport message |

Consumer logs and broker telemetry reflect discard vs requeue; `ingress.ack_failed_after_accept` applies only when accept succeeded but ack failed (separate path from policy classification).

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `ShouldRequeue_WhenRequeueOnFailureTrueAndDiscardException_ShouldReturnFalse` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ShouldRequeue_WhenRequeueOnFailureTrueAndTransientIOException_ShouldReturnTrue` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ShouldRequeue_WhenRequeueOnFailureFalse_ShouldReturnFalseForTransientFailure` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenStorageFull_ShouldDiscardWithoutRequeue` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenAuthorizationRejects_ShouldDiscardWithoutAccept` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
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

- `TargetInvocationException` and single-inner `AggregateException` unwrapping in integration scenarios.
- Every non-requeue exception type listed in policy (`InboxDispatchException`, `InboxStorageException`, and similar) exercised on a live broker.
- Kafka explicit requeue-off drain behavior.
- Azure Service Bus explicit requeue-off drain behavior.
- AMQP explicit requeue-off poison drain behavior (called out in ingress matrix).

### Out-of-Scope

- Dead-letter queue configuration (broker and ops concern).
- Inbox processor retry policy (processor axis).
- Custom exception taxonomy plug-in at runtime.

## Deep Docs

- [AMQP transport: Guarantees](../../integrations/amqp.md)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
