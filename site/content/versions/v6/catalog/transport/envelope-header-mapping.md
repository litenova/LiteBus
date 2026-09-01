# Envelope to Header Mapping

- **ID**: `transport.envelope-header-mapping`
- **Name**: Envelope to header mapping
- **Maturity**: GA
- **Summary**: Single mapper that converts durable envelope metadata to canonical transport headers for publish operations.

## What It Does

`TransportEnvelopeHeaderMapper.BuildHeaders(TransportEnvelopeHeaderSource)` is the shared outbound mapping point used by inbox and outbox dispatch adapters. It writes required identity headers and adds optional metadata headers only when values are present.

Ingress adapters perform reverse mapping with `TransportHeaderValues` and ingress-side guards. This page covers outbound mapping only.

## Public Surface

| API | Role |
| --- | --- |
| `TransportEnvelopeHeaderMapper.BuildHeaders(TransportEnvelopeHeaderSource)` | Build canonical outbound header dictionary |
| `TransportEnvelopeHeaderSource` | Input model carrying envelope metadata for mapping |

### Source-to-Header Mapping Table

| Source field | Header constant | Header name | Wire type | Rule |
| --- | --- | --- | --- | --- |
| `MessageId` (`Guid`) | `TransportHeaders.MessageId` | `litebus-message-id` | string | Always written as `Guid.ToString("D")` |
| `ContractName` (`string`) | `TransportHeaders.ContractName` | `litebus-contract-name` | string | Always written |
| `ContractVersion` (`int`) | `TransportHeaders.ContractVersion` | `litebus-contract-version` | number | Always written |
| `CorrelationId` (`string?`) | `TransportHeaders.CorrelationId` | `correlation-id` | string | Written only when not null or whitespace |
| `CausationId` (`string?`) | `TransportHeaders.CausationId` | `causation-id` | string | Written only when not null or whitespace |
| `TenantId` (`string?`) | `TransportHeaders.TenantId` | `tenant-id` | string | Written only when not null or whitespace |
| `TraceContext` (`string?`) | `TransportHeaders.TraceContext` | `litebus-trace-context` | string | Written only when not null or whitespace |
| `IdempotencyKey` (`string?`) | `TransportHeaders.IdempotencyKey` | `litebus-idempotency-key` | string | Written only when not null or whitespace |
| `VisibleAfter` (`DateTimeOffset?`) | `TransportHeaders.VisibleAfter` | `litebus-visible-after` | string | Written only when set; format `O` with invariant culture |

## Packages

- `LiteBus.Transport`
- `LiteBus.Transport.Abstractions` (header constants and types)

## Requires

- `transport.canonical-headers`
- Used by dispatch axis components that publish durable envelopes

## Invariants

- Required headers (`message-id`, `contract-name`, `contract-version`) are always written.
- Optional headers are omitted instead of written with null values.
- `VisibleAfter` uses ISO-8601 round-trip format (`"O"`).

## Non-Goals

- Reverse mapping from headers into ingress accept items.
- Contract validation beyond presence checks in ingress.
- Custom per-tenant header namespaces.

## Observability

- Mapped message and correlation values flow into `send {destination}` span attributes through adapter mapping.
- `litebus-trace-context` preserves distributed trace context payload for ingress-side continuation.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `BuildHeaders_ShouldMapAllMetadataFields` | `LiteBus.Transport.UnitTests` |
| `PublishAsync_WithLiteBusHeaders_PreservesHeaderValues` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`) |

### Untested

- Minimal-source mapping test that asserts omitted optional headers explicitly.
- Round-trip assertions for tenant and idempotency headers across each broker adapter.

### Out-of-Scope

- Ingress-side header interpretation and validation policy.
- Envelope body serialization.

## Deep Docs

- [canonical-headers.md](canonical-headers.md)
- [Architecture.md](../../architecture/README.md)
- [dispatch.transport-envelope-mapping](../dispatch/transport-envelope-mapping.md)
