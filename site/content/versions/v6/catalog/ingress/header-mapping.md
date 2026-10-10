# Wire Header to Accept Metadata

- **ID**: `ingress.header-mapping`
- **Name**: Wire header to accept metadata
- **Maturity**: GA
- **Summary**: Translates LiteBus transport headers and broker delivery ids into `InboxAcceptMetadata` on each accept.

## Purpose and Scope

`TransportInboxIngressMapper` builds `InboxAcceptMetadata` from `TransportMessage` headers and properties:

| Concern | Source | Default behavior |
| --- | --- | --- |
| Identity | Broker delivery id or `litebus-message-id` when trusted | Supplied identity from broker id; deterministic GUID when id is not a GUID |
| Idempotency | Broker-scoped key or `litebus-idempotency-key` when trusted | `ingress:{destination}:{brokerMessageId}` |
| Visibility | `litebus-visible-after`, `litebus-visible-after-delay` | Immediate; relative delay wins when both present |
| Trace | `correlation-id`, `causation-id`, `litebus-trace-context` | None, correlated, workflow, or distributed variants |
| Tenant | `tenant-id` when trusted | Unscoped unless trusted headers enabled |

Contract headers are validated separately: name required, version must be a positive integer.

## Mapping Order

1. Read contract headers (`litebus-contract-name`, `litebus-contract-version`) and validate.
2. Resolve identity and idempotency from trusted app headers or broker delivery id.
3. Resolve visibility from relative delay first, then absolute visible-after.
4. Resolve trace metadata from correlation, causation, and trace-context.
5. Resolve tenant scope only when trusted headers are enabled.

Relative visibility delay takes precedence over absolute visible-after when both are present.

## Public Surface

Internal mapper used by `TransportInboxIngressHandler`. Policy flags on `TransportInboxIngressMappingOptions` (from `TransportInboxIngressOptions`):

| Property | Default | Role |
| --- | --- | --- |
| `RequireStableIdentity` | `true` | Fail when broker delivery id is missing |
| `TrustApplicationHeaders` | `false` | Honor app idempotency, tenant, and message id headers |

Canonical wire header names on `TransportHeaders` (`LiteBus.Transport.Abstractions`): `litebus-contract-name`, `litebus-contract-version`, `litebus-message-id`, `litebus-idempotency-key`, `litebus-visible-after`, `litebus-visible-after-delay`, `tenant-id`, `causation-id`, `litebus-trace-context`, `correlation-id`.

## Identity and Idempotency Defaults

| Setting | Identity default | Idempotency default | Operational effect |
| --- | --- | --- | --- |
| `RequireStableIdentity=true`, `TrustApplicationHeaders=false` | Broker delivery id (or deterministic GUID from broker id string) | `ingress:{destination}:{brokerMessageId}` | Stable duplicate absorption on redelivery |
| `RequireStableIdentity=true`, `TrustApplicationHeaders=true` | `litebus-message-id` when valid GUID, else broker id | `litebus-idempotency-key` when present, else broker-scoped key | Upstream can control dedup key and identity |
| `RequireStableIdentity=false`, broker id missing | Generated identity | `Idempotency.None` | No broker-scoped dedup on retry |

## Broker Notes

Every ingress adapter uses this same mapper. Broker pages differ in test coverage depth, not in mapping rules:

- AMQP: richest mapping validation via unit tests and AMQP ingress integration tests.
- Kafka: no dedicated header edge-case matrix in broker Docker tests.
- AWS SQS: partial header edge-case matrix in broker Docker tests.
- Azure Service Bus: header edge-case matrix not yet covered in emulator ingress tests.
- InMemory: full edge-case matrix used as cross-broker reference behavior.

## Packages

- `LiteBus.Inbox.Ingress`
- `LiteBus.Transport.Abstractions` (header constants)

## Requires

- `ingress.safety-options` (mapping policy flags)
- Publishers that emit LiteBus wire headers on dispatch or external producers

## Invariants

- Missing broker delivery id fails closed when `RequireStableIdentity` is true (default).
- Untrusted mode ignores application idempotency and tenant headers; broker delivery id drives deduplication.
- Broker-scoped idempotency keys include destination so the same broker id on different queues does not collide.

## Non-Goals

- Payload schema validation beyond contract registration and JSON deserialization.
- Cross-broker identity federation.
- Outbox header mapping (dispatch axis).

## Observability

No mapping-specific metrics or activities. Failures surface as exceptions (`InboxIngressException`, `TransportHeaderMappingException`) logged by `ingress.transport-consumer` when discard or requeue runs.

Contract header validation (missing name, non-positive version) throws before mapping completes; poison paths are classified by `ingress.ack-policy` and appear in consumer logs without dedicated mapping counters.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `ToInboxAcceptMetadata_ShouldRoundTripDispatchHeaders` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_ShouldMapOptionalHeadersWhenTrusted` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_ShouldMapVisibleAfterDelayHeader` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_ShouldDefaultToBrokerScopedIdempotency` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `ToInboxAcceptMetadata_WhenBrokerIdMissingAndRequired_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `AcceptAsync_WhenMessageIdHeaderInvalid_ShouldLeaveInboxIdUnset` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_WhenVisibleAfterHeaderInvalid_ShouldIgnoreVisibleAfter` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `DuplicateMessageId_ShouldCreateSingleInboxRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `DuplicateBrokerDelivery_ShouldExecuteHandlerOnce` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `MissingContractName_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `MissingContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `WrongContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `InvalidMessageId_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InvalidMessageId_ShouldAcceptWithGeneratedInboxId` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `WrongClrShape_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |

### Untested

- `litebus-trace-context` distributed variant through a live broker round trip.
- Correlation and causation headers asserted on persisted envelope fields in integration tests (mapper unit tests cover mapping only).
- Azure Service Bus header edge cases on the wire.
- Missing contract version and wrong CLR shape matrix in AWS and AMQP broker suites (covered in InMemory matrix).
- Kafka broker suite assertions for header edge cases beyond unknown contract and invalid JSON.

### Out-of-Scope

- Payload schema validation beyond contract registration and JSON deserialization.
- Cross-broker identity federation.
- Outbox header mapping (dispatch axis).

## Deep Docs

- [Inbox: Ingress](../../reliable-messaging/inbox.md)
- [AMQP transport: Wire headers](../../integrations/amqp.md)
