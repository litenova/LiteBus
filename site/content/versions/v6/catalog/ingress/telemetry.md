# Ingress OpenTelemetry Metrics

- **ID**: `ingress.telemetry`
- **Name**: Ingress OpenTelemetry Metrics
- **Maturity**: GA
- **Summary**: Exposes stable ingress instrument names on the inbox meter for broker ack failures after successful accept.

## Purpose and Scope

`LiteBusInboxIngressTelemetry` defines public instrument constants. `TransportInboxIngressTelemetry` increments `ingress.ack_failed_after_accept` when `TransportMessage.AcceptAsync` fails after the inbox store already accepted the delivery.

Register the inbox meter through `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry` (or axis-specific transport OpenTelemetry packages for transport-side metrics).

## Public Surface

| Constant / member | Value / role |
| --- | --- |
| `LiteBusInboxIngressTelemetry.MeterName` | `LiteBus.Inbox` |
| `LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName` | `ingress.ack_failed_after_accept` |
| `TransportInboxIngressTelemetry.RecordAckFailedAfterAccept()` | Internal recording site (consumer after accept-then-ack failure) |
| `MeterProviderBuilder.AddLiteBusInboxMetrics()` | Subscribes inbox meter including ingress counter |
| `TracerProviderBuilder.AddLiteBusTransportInstrumentation()` | Transport process activities from the consumer invoker |

## Instrument Contract

Public telemetry names here are treated as API contract. Renaming `LiteBusInboxIngressTelemetry.MeterName` or `LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName` is a breaking change for dashboards and alerts.

## Packages

- `LiteBus.Inbox.Ingress` (instrument constants and recording)
- `LiteBus.Inbox.Extensions.OpenTelemetry` (meter registration)

## Requires

- OpenTelemetry host registration (hosting axis)
- `ingress.transport-consumer` (recording site)

## Invariants

- Public instrument names are part of the consumer contract; renames are breaking changes.
- Counter increments once per ack failure after accept, not per redelivery attempt at the store.
- Counter recording is decoupled from requeue success, it records on ack failure before return-to-queue attempt.

## Non-Goals

- Distributed tracing activities dedicated to ingress (roadmap item; transport consume activities exist today).
- Per-broker ingress dashboards in-box.
- Health check mapping for ack failures (application-owned probes).

## Observability

Ingress ships one public counter on the shared inbox meter. Transport process tracing is documented under the transport axis; the consumer invoker starts `process {destination}` spans.

### Inbox Meter Instruments

| Instrument | Type | Constant | When incremented | Recording site |
| --- | --- | --- | --- | --- |
| `ingress.ack_failed_after_accept` | Counter | `LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName` | Broker `AcceptAsync` throws after `IInbox` accept succeeded | `TransportInboxIngressTelemetry.RecordAckFailedAfterAccept()` in `TransportInboxIngressConsumer` |

| Meter | Constant | Registration |
| --- | --- | --- |
| `LiteBus.Inbox` | `LiteBusInboxIngressTelemetry.MeterName` | `MeterProviderBuilder.AddLiteBusInboxMetrics()` |

After recording, the consumer logs EventId 3004 (`AckFailedAfterAccept`) and requeues so idempotent accept absorbs redelivery.

## Broker Interpretation

| Broker | Typical meaning of ack failure after accept |
| --- | --- |
| AMQP | Ack or channel operation failed after inbox row write |
| Kafka | Offset commit or acknowledgement path failed after inbox row write |
| AWS SQS | Delete or ack operation failed after inbox row write |
| Azure Service Bus | Complete or equivalent ack operation failed after inbox row write |
| InMemory | In-memory transport acknowledgement failed after inbox row write |

### Related Transport Signals (Not Ingress Instruments)

| Signal | Role |
| --- | --- |
| `process {destination}` activity | Per delivery in the consumer invoker; not an ingress meter |
| `litebus.transport.circuit_breaker.*` | Breaker state from the explicitly registered root transport |

### Registration Checklist

1. Install `LiteBus.Inbox.Extensions.OpenTelemetry`.
2. Call `AddLiteBusInboxMetrics()` on the meter provider builder.
3. For process spans, call `AddLiteBusTransportInstrumentation()`.

## Test Coverage

### Covered

`TransportInboxIngressConsumerTests.HandleDeliveryAsync_WhenAckFailsAfterAccept_ShouldNotDiscard` attaches a `MeterListener` to `LiteBus.Inbox`, forces broker acknowledgement failure after a recorded inbox accept, and asserts that `ingress.ack_failed_after_accept` increments before the delivery is returned to the queue.

### Untested

- `LiteBusInboxIngressTelemetry.MeterName` registration through `AddLiteBusInboxMetrics`.
- Counter semantics on duplicate redelivery attempts at the store.
- Per-broker metric assertions in Kafka, AWS SQS, Azure Service Bus, and InMemory integration suites.

### Out-of-Scope

- Distributed tracing activities dedicated to ingress (transport consume activities exist today).
- Per-broker ingress dashboards in-box.
- Health check mapping for ack failures.

## Deep Docs

- [Architecture: OpenTelemetry metric catalog](../../architecture/README.md)
- [Inbox: ack failure note](../../reliable-messaging/inbox.md)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
