# Kafka Inbox Ingress

- **ID**: `ingress.kafka`
- **Name**: Kafka inbox ingress
- **Maturity**: Beta
- **Summary**: Consumes Kafka topics and accepts records into the inbox through shared transport ingress.

## Purpose and Scope

`UseKafkaIngress` registers `KafkaInboxIngressModule`. The module maps the topic destination, requeue policy, and shared safety record into `TransportInboxIngressOptions`. A root `KafkaTransportModule` is required.

Kafka consumer offsets commit after the successful accept and acknowledgement path. The Confluent.Kafka consume loop returns one record per call, so `KafkaInboxIngressOptions` does not expose a prefetch field that the adapter cannot honor. Provider-neutral limits remain available through `Safety`.

## Beta Rationale

Kafka ingress is marked Beta because the core intake path is stable, but parity coverage is still narrower than AMQP in two areas:

- Kafka-specific offset and group behavior requires more live-provider coverage.
- Matrix depth parity (header edge-case and requeue-off matrices are intentionally incomplete in broker Docker tests).

## Public Surface

```csharp
builder.AddKafkaTransport(new KafkaTransportOptions { BootstrapServers = "localhost:9092" });

inbox.UseKafkaIngress(ingress =>
{
    ingress.UseOptions(new KafkaInboxIngressOptions
    {
        Destination = "orders.commands",
        RequeueOnFailure = true
    });
});
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseKafkaIngress(Action<KafkaInboxIngressModuleBuilder>)` | Registration extension |
| `KafkaInboxIngressModuleBuilder.UseOptions(KafkaInboxIngressOptions)` | Topic, failure behavior, and shared safety settings |
| `KafkaInboxIngressModuleBuilder.ConfigureHost(Action<TransportInboxIngressHostOptions>)` | Retry and enablement |
| `KafkaInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without consumer loop |
| `KafkaInboxIngressModule` | Child module |

## Kafka Options and Parity vs AMQP

| Capability | Kafka ingress | AMQP ingress |
| --- | --- | --- |
| Destination required | `Destination` required | `QueueName` required |
| Root transport required | `AddKafkaTransport(...)` | `AddAmqpTransport(...)` |
| Native receive control | none | `PrefetchCount` |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| Shared `Safety` settings | yes | yes |
| Declare destination knobs | no | yes |

## Packages

- `LiteBus.Inbox.Ingress.Kafka`
- `LiteBus.Transport.Kafka`

## Requires

- `ingress.registration`
- Shared ingress capabilities
- `transport.kafka`
- Inbox storage and contracts

## Invariants

- `Destination` (topic) and a root Kafka transport are required at compose time.
- Consumer group and offset semantics follow `KafkaTransportOptions` (transport axis).
- Idempotency uses broker record identifiers mapped through shared header mapping when present.
- Identity and idempotency default to broker-scoped values (`Safety.RequireStableIdentity=true`, `Safety.TrustApplicationHeaders=false`).

## Non-Goals

- GA production tier declaration (Beta per v6 feature index).
- Full parity with AMQP ingress option surface on the Kafka builder.
- Kafka outbox ingress (outbox uses dispatch).

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | Offset commit / ack fails after inbox accept |

Meter `LiteBus.Inbox` via `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Registration |
| --- | --- |
| `process {destination}` activity | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.*` with tag `kafka` | `AddLiteBusTransportMetrics()` for the root Kafka transport |

### Structured Logs

EventId 3002, 3003, 3004 from shared ingress consumer (`TransportInboxIngressLogMessages`).

## Identity, Idempotency, and Headers

Kafka ingress inherits shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | Missing broker delivery id fails closed |
| `Safety.TrustApplicationHeaders` | false | Broker id drives identity and idempotency key |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | Duplicate redelivery can absorb into existing inbox row |

`KafkaInboxIngressOptions.Safety` exposes the same trust, authorization, admission, and batch settings as the other ingress adapters.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `UnknownContract_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `InvalidJson_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `StoreFull_ShouldNotIncreasePendingRowsBeyondCapacity` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `TransientAcceptFailure_ShouldRedeliverSameOffsetWithoutRestart` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` (Kafka case) | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

Shared broker infrastructure: `LiteBus.Transport.IntegrationTesting` (`KafkaBrokerHost`, Redpanda default, `KafkaIngressTestSupport`).

### Untested (Kafka Docker; Covered Elsewhere)

- Duplicate MessageId idempotency (`LiteBus.Durable.IntegrationTests/Ingress/InMemory/`).
- Header edge cases (missing contract name, wrong version, invalid MessageId) (`Ingress/InMemory/`).
- `RequeueOnFailure = false` poison drain and explicit on/off pair (`Ingress/InMemory/`, `Ingress/AwsSqs/`).
- `KafkaInboxIngressModuleBuilder.ConfigureHost` retry interval tuning under live broker load.
- Offset commit failure after accept with `ingress.ack_failed_after_accept` metric assertion.
- Azure and AWS style header edge-case parity inside Kafka broker suite itself.

### Out-of-Scope

- GA production tier declaration (Beta per v6 feature index).
- Full parity with AMQP ingress option surface on the Kafka builder.
- Kafka outbox ingress (outbox uses dispatch).

## Deep Docs

- [Kafka transport](../../integrations/kafka.md)
- [Integration tests](../../testing/integration-tests.md) (Kafka harness notes)
- [v6 feature index: Ingress](../../reference/feature-index-v6.md)
