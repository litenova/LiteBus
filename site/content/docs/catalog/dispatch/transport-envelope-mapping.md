# Transport Envelope Mapping

- **ID**: `dispatch.transport-envelope-mapping`
- **Name**: Transport envelope mapping
- **Maturity**: GA
- **Summary**: Copy durable envelope metadata (contract, trace, tenant, idempotency) onto transport publish headers so downstream consumers and ingress can reconstruct context.

## What It Does

`InboxTransportEnvelopeMapper` and `OutboxTransportEnvelopeMapper` (internal in dispatch packages) delegate to `TransportEnvelopeHeaderMapper` in `LiteBus.Transport`. They build a header dictionary from the envelope's message id, contract name and version, correlation and causation ids, tenant id, trace context, idempotency key, and visible-after scheduling.

Ingress adapters on the receiving side read the same header conventions to populate `InboxAcceptMetadata` when accepting into a remote inbox. The headers are symmetric, so an outbox publish on one service becomes an ingress accept on another, and no team needs its own header names.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch` | Inbox-side mapper |
| `LiteBus.Outbox.Dispatch` | Outbox-side mapper |
| `LiteBus.Transport` | Shared header mapping |
| `LiteBus.Transport.Abstractions` | `TransportHeaders` name constants |

## Requires

- `dispatch.transport-core` (mappers run inside transport dispatchers)
- `runtime.contract-registry` (contract name/version on envelope)

## Invariants

- Contract identity on the wire uses the stable contract name and version, not CLR type names.
- The message body remains the serialized payload from storage. Headers carry metadata only.
- Header mapping lives in dispatch and ingress adapter packages, not in storage or core processors.
- FIFO addressing (`MessageGroupId`, `DeduplicationKey`) travels as `TransportPublishRequest` properties set by the outbox dispatcher, not as headers.

## Non-Goals

- It does not define broker-specific property bags beyond what `ITransportPublisher` abstracts.
- It does not validate header round-trip at dispatch time. That is a consumer and ingress responsibility.
- It does not encrypt header values. Payload protection applies to the body only.

## Public Surface

```csharp
var headers = TransportEnvelopeHeaderMapper.BuildHeaders(new TransportEnvelopeHeaderSource(
    messageId,
    "orders.ship",
    1,
    correlationId,
    causationId,
    tenantId,
    traceContext,
    idempotencyKey,
    visibleAfter));
```

### `InboxTransportEnvelopeMapper.BuildHeaders(InboxEnvelope)` (Internal)

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch` |
| Visibility | Internal, invoked from `TransportInboxDispatcher.DispatchAsync` |
| Returns | A `Dictionary<string, object?>` passed to `TransportPublishRequest.Headers` |

### `OutboxTransportEnvelopeMapper.BuildHeaders(OutboxEnvelope)` (Internal)

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch` |
| Visibility | Internal, invoked from `TransportOutboxDispatcher.DispatchAsync` |

### `TransportEnvelopeHeaderMapper.BuildHeaders(TransportEnvelopeHeaderSource)`

| | |
| --- | --- |
| Package | `LiteBus.Transport` |
| Visibility | Public shared mapper |
| Headers always set | `TransportHeaders.MessageId`, `ContractName`, `ContractVersion` |
| Headers when present | `CorrelationId`, `CausationId`, `TenantId`, `TraceContext`, `IdempotencyKey`, `VisibleAfter` (ISO 8601) |

### `TransportPublishRequest.Headers`

It is the wire carrier on publish. Broker adapters map dictionary entries to AMQP properties, Kafka record headers, SQS message attributes, or Service Bus application properties.

## Observability

| Signal | Role |
| --- | --- |
| `send {destination}` activity tags | The `messaging.message.id`, `messaging.destination.name`, conversation id, and route attributes from the publish request |
| Trace context header | `TransportHeaders.TraceContext` is copied to the wire, so downstream `process {destination}` can continue or link the stored W3C context |
| Correlation id | Set on `TransportPublishRequest.CorrelationId` and duplicated in the headers when the envelope carries a value |

No dedicated header-mapping counter or meter. Mapping failures throw dispatch exceptions before publish.

## References

- [Architecture.md: Transport platform](../../architecture/README.md#transport-platform)
- [Reliable-Messaging-Semantics.md](../../reliable-messaging/semantics.md)
- [transport.canonical-headers](../transport/canonical-headers.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `BuildHeaders_ShouldMapAllMetadataFields` | `LiteBus.Inbox.UnitTests` (`Dispatch/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`) |
| `DispatchAsync_ShouldPublishEnvelopeThroughTransport` | `LiteBus.Inbox.UnitTests` (`Dispatch/`) |
| `BuildHeaders_ShouldMapAllMetadataFields` | `LiteBus.Outbox.UnitTests` (`Dispatch/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `DispatchAsync_ShouldPublishEnvelopeThroughTransport` | `LiteBus.Outbox.UnitTests` (`Dispatch/`) |

### Untested

- Direct unit tests for `TransportEnvelopeHeaderMapper.BuildHeaders`.
- Idempotency key round-trip through full remote ingress acceptance.
- `VisibleAfter` interpretation in dispatch-only suites.
- Header mapping behavior with encrypted payload bodies.

### Out-of-Scope

- Broker-specific property bags beyond the `ITransportPublisher` abstraction
- Dispatch-time validation of header round-trip, which is a consumer and ingress responsibility
- Encrypting header values. Payload protection applies to the body only.
