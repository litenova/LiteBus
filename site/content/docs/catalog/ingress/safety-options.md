# Ingress Safety and Authorization Options

- **ID**: `ingress.safety-options`
- **Name**: Ingress Safety and Authorization Options
- **Maturity**: GA
- **Summary**: Configures body limits, identity requirements, trusted headers, requeue behavior, and optional delivery authorization on `TransportInboxIngressOptions`.

## Purpose and Scope

`TransportInboxIngressOptions` is the shared safety record registered by every ingress adapter. Broker-specific option types map a subset of fields into this record at module build time. The handler and consumer read these options for size checks, mapping policy, requeue defaults, destination subscription settings, and optional edge authorization.

## Option Model

| Type / member | Default | Role |
| --- | --- | --- |
| `TransportInboxIngressOptions.Destination` | (required) | Queue, topic, or channel name |
| `TransportInboxIngressOptions.PrefetchCount` | 0 | Unacknowledged prefetch / batch size hint |
| `TransportInboxIngressOptions.DeclareDestination` | false | Declare queue or topic before subscribe (AMQP) |
| `TransportInboxIngressOptions.DurableDestination` | false | Survive broker restart when declaring (AMQP) |
| `TransportInboxIngressOptions.RequeueOnFailure` | true | Requeue on transient accept failures |
| `TransportInboxIngressOptions.MaxMessageBytes` | 4 MiB (`DefaultMaxMessageBytes`) | Reject oversized bodies before deserialize |
| `TransportInboxIngressOptions.RequireStableIdentity` | true | Fail when broker delivery id is missing |
| `TransportInboxIngressOptions.TrustApplicationHeaders` | false | Honor app idempotency, tenant, and message id headers |
| `TransportInboxIngressOptions.AuthorizeDeliveryAsync` | null | Host callback before accept |
| `TransportInboxIngressOptions.EnableBatchAccept` | false | Enable buffered batch accepts |
| `TransportInboxIngressOptions.BatchMaxWait` | 200 ms | Partial batch flush delay |

## Broker Option Parity

| Broker options type | Exposes trust headers | Exposes batch options | Exposes declare or durable destination |
| --- | --- | --- | --- |
| `AmqpInboxIngressOptions` | yes | yes | yes |
| `KafkaInboxIngressOptions` | no | no | no |
| `AwsSqsInboxIngressOptions` | no | no | no |
| `AzureServiceBusInboxIngressOptions` | no | no | no |
| `InMemoryInboxIngressOptions` | no | no | no |

## Public Surface

- `TransportInboxIngressOptions` (shared options record)
- `AmqpInboxIngressOptions`
- `KafkaInboxIngressOptions`
- `AwsSqsInboxIngressOptions`
- `AzureServiceBusInboxIngressOptions`
- `InMemoryInboxIngressOptions`

## Packages

- `LiteBus.Inbox.Ingress`
- Broker option records in each `LiteBus.Inbox.Ingress.*` package

## Requires

- `ingress.registration` to register options in DI

## Invariants

- `AuthorizeDeliveryAsync` exceptions follow the same requeue and discard path as store failures.
- `MaxMessageBytes` check runs before deserialization.
- `TrustApplicationHeaders` must stay false unless the broker binding authenticates upstream publishers.
- `RequireStableIdentity=true` and broker message ids produce deterministic duplicate absorption across retries.

## Non-Goals

- Authentication or authorization framework (host supplies `AuthorizeDeliveryAsync`).
- Per-tenant broker bindings (application concern).
- Store retention or dead-letter tuning (inbox storage axis).

## Observability

No per-option metrics. Safety enforcement surfaces through exceptions and consumer behavior:

| Option violation | Observable signal |
| --- | --- |
| `MaxMessageBytes` exceeded | Exception before store; consumer discard or requeue per `ingress.ack-policy` |
| `RequireStableIdentity` with missing broker id | `InboxIngressException` from mapper; discard when non-requeue exception |
| `AuthorizeDeliveryAsync` rejection | Same ack policy path as store failure (no dedicated counter) |
| `RequeueOnFailure = false` | Poison messages discarded; broker queue drains |

Oversized or unauthorized deliveries appear in unstructured or structured consumer logs when the consumer handles the failure. No `litebus.inbox.*` instruments at the ingress edge.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `AcceptAsync_WhenBodyExceedsMaxMessageBytes_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `AcceptAsync_WithAuthorizationCallback_ShouldAuthorizeBeforeStoreWrite` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenAuthorizationRejects_ShouldDiscardWithoutAccept` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_ShouldMapOptionalHeadersWhenTrusted` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_WhenBrokerIdMissingAndRequired_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenTransientFailureAndRequeueEnabled_ShouldReturnToQueue` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ShouldRequeue_WhenRequeueOnFailureFalse_ShouldReturnFalseForTransientFailure` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`, `Ingress/AzureServiceBus/`, `Ingress/AwsSqs/`, `Ingress/InMemory/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDrainQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`, `Ingress/InMemory/`) |
| `OutboxToInbox_ShouldPublishProcessAndDispatchCommand` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` (uses `TrustApplicationHeaders = true`) |

### Untested

- `DeclareDestination` and `DurableDestination` AMQP queue declaration flags.
- `MaxMessageBytes = 0` (disabled) behavior.
- Beta broker builders exposing `TrustApplicationHeaders`, `EnableBatchAccept`, or `MaxMessageBytes` (not on Kafka, Azure, or AWS option types today).
- Runtime assertions for trusted-header override behavior on live brokers other than AMQP.

### Out-of-Scope

- Authentication or authorization framework (host supplies `AuthorizeDeliveryAsync`).
- Per-tenant broker bindings.
- Store retention or dead-letter tuning (storage axis).

## Deep Docs

- [Inbox: Ingress options table](../../reliable-messaging/inbox.md)
- [Inbox AMQP ingress](../../integrations/inbox-amqp-ingress.md)
