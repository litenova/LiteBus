# Canonical Wire Headers

- **ID**: `transport.canonical-headers`
- **Name**: Canonical wire headers
- **Maturity**: GA
- **Summary**: Stable header name contract and typed value readers used across AMQP, Kafka, SQS, Azure Service Bus, and in-memory adapters.

## What It Does

`TransportHeaders` defines the canonical header names that dispatch writes and ingress reads. `TransportHeaderValues` provides broker-tolerant value coercion for string and integer reads, including UTF-8 byte arrays and numeric conversions.

Canonical headers are transport contract surface. Renaming them breaks wire compatibility.

## Public Surface

| Type | Member | Role |
| --- | --- | --- |
| `TransportHeaders` | Header constants | Canonical wire names |
| `TransportHeaderValues` | `GetString(headers, name)` | Typed string read |
| `TransportHeaderValues` | `GetInt32(headers, name)` | Typed integer read |
| `TransportHeaderValues` | `ConvertToString(value)` | Broker-safe value conversion |
| `TransportHeaderMappingException` | exception type | Missing required header guard |

### Full Canonical Header Table

| Constant | Header name | Type on wire | Read rule |
| --- | --- | --- | --- |
| `MessageId` | `litebus-message-id` | string | Required for durable identity when present |
| `ContractName` | `litebus-contract-name` | string | Required for ingress contract resolution |
| `ContractVersion` | `litebus-contract-version` | number or string | Parsed by `GetInt32` |
| `CorrelationId` | `correlation-id` | string | Preferred correlation header |
| `CausationId` | `causation-id` | string | Upstream causal message id |
| `TenantId` | `tenant-id` | string | Tenant partition metadata |
| `TraceContext` | `litebus-trace-context` | string | Distributed trace context payload |
| `IdempotencyKey` | `litebus-idempotency-key` | string | Durable idempotency key |
| `VisibleAfter` | `litebus-visible-after` | ISO-8601 string | Absolute visibility timestamp |
| `VisibleAfterDelay` | `litebus-visible-after-delay` | number or string | Relative visibility delay seconds |
| `ContentEncoding` | `litebus-content-encoding` | string | Payload encoding marker (`base64` for SQS binary path) |

### Conversion Rules (`TransportHeaderValues`)

| Input value | `GetString` | `GetInt32` |
| --- | --- | --- |
| `string` | returned as-is | parsed with invariant culture |
| `byte[]` | UTF-8 decode | parsed when length is 1 or 4 |
| `ReadOnlyMemory<byte>` / `Memory<byte>` | UTF-8 decode | not used directly for int parsing |
| `int`, `byte`, `sbyte`, `short` | invariant conversion | converted directly |
| `long` | invariant conversion | accepted when in Int32 range |
| other values | invariant `Convert.ToString` | rejected (`null`) |

## Packages

- `LiteBus.Transport.Abstractions`
- AMQP value conversion through `AmqpHeaderValues`; header names come from `TransportHeaders`

## Requires

- None

## Invariants

- Header names are public wire contract and must remain stable.
- Legacy `CorrelationId` is read fallback only, not default write target.
- Header readers apply invariant culture and broker-safe coercion.

## Non-Goals

- Payload serialization format selection.
- Header encryption and signing.
- Broker-specific metadata namespaces beyond canonical mapping.

## Observability

- `litebus-trace-context` is the transport header used for trace propagation payload.
- Send and process spans use mapped message and correlation identifiers.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `CanonicalHeaders_ShouldUseStableWireNames` | `LiteBus.Transport.UnitTests` |
| `GetString_ShouldReadByteEncodedHeaders` | `LiteBus.Transport.UnitTests` |
| `GetString_ShouldConvertSupportedHeaderValueTypes` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `GetInt32_ShouldParseNumericHeaderVariants` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ToSendMessageRequest_WithBinaryBody_ShouldBase64Encode` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToTransportMessage_WithBase64Body_ShouldDecodeBytes` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `MissingContractName_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`, `Ingress/AwsSqs/`) |

### Untested

- End-to-end canonical correlation header coverage on each broker.
- Explicit round-trip checks for `VisibleAfter` and `VisibleAfterDelay` on all adapters.

### Out-of-Scope

- Custom encryption and signed header envelopes.
- Non-canonical broker metadata key spaces.

## Deep Docs

- [envelope-header-mapping.md](envelope-header-mapping.md)
- [Architecture.md](../../architecture/README.md)
- [Aws-Sqs-Transport.md](../../integrations/aws-sqs.md)
