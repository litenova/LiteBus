# Wire Header to Accept Metadata

- **ID**: `ingress.header-mapping`
- **Name**: Wire header to accept metadata
- **Maturity**: GA
- **Summary**: This mapper translates LiteBus transport headers and broker delivery ids into `InboxAcceptMetadata` on each accept.

## Purpose and Scope

`TransportInboxIngressMapper` builds `InboxAcceptMetadata` from `TransportMessage` headers and properties:

| Concern | Source | Default behavior |
| --- | --- | --- |
| Identity | Broker delivery id or `litebus-message-id` when trusted | Supplied identity from broker id, with GUID ids verbatim and other ids as a destination-scoped deterministic GUID |
| Idempotency | Broker-scoped key or `litebus-idempotency-key` when trusted | `ingress:{destination}:{brokerMessageId}` |
| Visibility | `litebus-visible-after`, `litebus-visible-after-delay` when trusted | Immediate unless trusted, with relative delay winning when both are present |
| Trace | `correlation-id`, `causation-id`, `litebus-trace-context` (correlation falls back to the message `CorrelationId` property) | None, correlated, workflow, or distributed variants, with correlation headers always honored |
| Tenant | `tenant-id` when trusted | Unscoped unless trusted headers are enabled |

Contract headers are validated separately. The name is required, and the version must be a positive integer.

## Mapping Order

1. Read contract headers (`litebus-contract-name`, `litebus-contract-version`) and validate.
2. Resolve identity and idempotency from trusted app headers or broker delivery id.
3. Resolve visibility from relative delay first, then absolute visible-after, only when trusted. Distrusted deliveries are immediately visible.
4. Resolve trace metadata from correlation, causation, and trace-context.
5. Resolve tenant scope only when trusted headers are enabled.

When both are present, the relative visibility delay takes precedence over the absolute visible-after. Correlation headers are always honored, even when application headers are distrusted.

## Public Surface

The mapper is internal and used by `TransportInboxIngressHandler`. Policy flags live on `TransportInboxIngressMappingOptions` (from `Safety` on `TransportInboxIngressOptions`):

| Property | Default | Role |
| --- | --- | --- |
| `Safety.RequireStableIdentity` | `true` | Fail when the broker delivery id is missing |
| `Safety.TrustApplicationHeaders` | `false` | Honor app idempotency, tenant, message id, and visibility headers |

The canonical wire header names on `TransportHeaders` (`LiteBus.Transport.Abstractions`) are `litebus-contract-name`, `litebus-contract-version`, `litebus-message-id`, `litebus-idempotency-key`, `litebus-visible-after`, `litebus-visible-after-delay`, `tenant-id`, `causation-id`, `litebus-trace-context`, and `correlation-id`.

## Identity and Idempotency Defaults

| Setting | Identity default | Idempotency default | Operational effect |
| --- | --- | --- | --- |
| `RequireStableIdentity=true`, `TrustApplicationHeaders=false` | Broker delivery id (GUID verbatim, other ids as a destination-scoped deterministic GUID) | `ingress:{destination}:{brokerMessageId}` | Duplicates are absorbed on redelivery |
| `RequireStableIdentity=true`, `TrustApplicationHeaders=true` | `litebus-message-id` when valid GUID, else broker id | `litebus-idempotency-key` when present, else broker-scoped key | Upstream can control the deduplication key and identity |
| `RequireStableIdentity=false`, broker id missing | Generated identity | `Idempotency.None` | No broker-scoped deduplication on retry |

## Broker Notes

Every ingress adapter uses this same mapper. Broker pages differ in test coverage depth, not in mapping rules:

- AMQP validates mapping through unit tests and AMQP ingress integration tests. The destination segment of the broker-scoped key is the consuming queue (`AmqpReceivedMessage.QueueName`, falling back to the exchange when unknown), so two queues bound to one exchange do not share an idempotency key.
- Kafka has no dedicated header edge-case matrix in broker Docker tests.
- AWS SQS has a partial header edge-case matrix in broker Docker tests.
- Azure Service Bus has no header edge-case matrix in emulator ingress tests yet.
- InMemory carries the full edge-case matrix as cross-broker reference behavior.

## Packages

- `LiteBus.Inbox.Ingress`
- `LiteBus.Transport.Abstractions` (header constants)

## Requires

- `ingress.safety-options` (mapping policy flags)
- Publishers that emit LiteBus wire headers on dispatch or external producers

## Invariants

- A missing broker delivery id fails closed when `RequireStableIdentity` is true (default).
- In untrusted mode, the mapper ignores application idempotency, tenant, and visibility headers. The broker delivery id drives deduplication, and the message is immediately visible.
- Broker-scoped idempotency keys include the destination, so the same broker id on different queues does not collide.
- Non-GUID broker delivery ids derive destination-scoped inbox identity, so the same id on different destinations names different messages. GUID ids stay verbatim.
- Correlation headers are always honored, even in untrusted mode.

## Non-Goals

- Payload schema validation beyond contract registration and JSON deserialization.
- Cross-broker identity federation.
- Outbox header mapping (dispatch axis).

## Observability

Mapping has no metrics or activities of its own. Failures surface as exceptions (`InboxIngressException`, `TransportHeaderMappingException`) logged by `ingress.transport-consumer` when the consumer discards or requeues.

Contract header validation (missing name, non-positive version) throws before mapping completes. Poison paths are classified by `ingress.ack-policy` and appear in consumer logs without dedicated mapping counters.

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
| `AcceptAsync_FromTwoQueuesBoundToOneExchange_ShouldProduceTwoRowsWithQueueScopedKeys` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_WithoutQueueName_ShouldFallBackToExchange` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `DuplicateMessageId_ShouldCreateSingleInboxRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `DuplicateBrokerDelivery_ShouldExecuteHandlerOnce` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `MissingContractName_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `MissingContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `WrongContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `InvalidMessageId_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `InvalidMessageId_ShouldAcceptWithGeneratedInboxId` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Ingress/AwsSqs/`) |
| `WrongClrShape_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |

### Untested

- The `litebus-trace-context` distributed variant through a live broker round trip.
- Assertions that correlation and causation headers reach persisted envelope fields in integration tests (mapper unit tests cover mapping only).
- Azure Service Bus header edge cases on the wire.
- Missing contract version and wrong CLR shape matrix in the AWS and AMQP broker suites (covered in the InMemory matrix).
- Kafka broker suite assertions for header edge cases beyond unknown contract and invalid JSON.

### Out-of-Scope

- Payload schema validation beyond contract registration and JSON deserialization.
- Cross-broker identity federation.
- Outbox header mapping (dispatch axis).

## References

- [Inbox: Ingress](../../reliable-messaging/inbox.md)
- [AMQP transport: Wire headers](../../integrations/amqp.md)
