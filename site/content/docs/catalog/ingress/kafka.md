# Kafka Inbox Ingress

- **ID**: `ingress.kafka`
- **Name**: Kafka inbox ingress
- **Maturity**: Beta
- **Summary**: This adapter consumes Kafka topics and accepts records into the inbox through shared transport ingress.

## Purpose and Scope

`UseKafkaIngress` registers `KafkaInboxIngressModule`. The module maps the topic destination, the requeue policy, and the shared safety record into `TransportInboxIngressOptions`. A root `KafkaTransportModule` is required.

Kafka consumer offsets commit after the accept and acknowledgement path succeeds. The Confluent.Kafka consume loop returns one record per call, so `KafkaInboxIngressOptions` has no prefetch field the adapter could not honor. Provider-neutral limits remain available through `Safety`, and the enforced `Safety.MaxInFlightMessages` bound is 1.

## Beta Rationale

Kafka ingress is Beta because its core intake path is stable but its parity coverage is narrower than AMQP in two areas:

- Kafka-specific offset and group behavior requires more live-provider coverage.
- Matrix depth parity is still missing, because the header edge-case and requeue-off matrices are intentionally incomplete in broker Docker tests.

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
- When present, broker record identifiers feed idempotency through shared header mapping.
- Identity and idempotency default to broker-scoped values (`Safety.RequireStableIdentity=true`, `Safety.TrustApplicationHeaders=false`).

## Non-Goals

- GA production tier declaration (the current tier is Beta in the Feature Index).
- Full parity with AMQP ingress option surface on the Kafka builder.
- Kafka outbox ingress (outbox uses dispatch).

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | Offset commit / ack fails after inbox accept |

The meter is `LiteBus.Inbox`, registered through `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Registration |
| --- | --- |
| `process {destination}` activity | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.*` with tag `kafka` | `AddLiteBusTransportMetrics()` for the root Kafka transport |

### Structured Logs

Structured logs come from the shared ingress consumer (`TransportInboxIngressLogMessages`) as EventId 3002, 3003, and 3004.

## Identity, Idempotency, and Headers

Kafka ingress inherits shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | A missing broker delivery id fails closed |
| `Safety.TrustApplicationHeaders` | false | Broker id drives identity and idempotency key |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | A duplicate redelivery can absorb into the existing inbox row |

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

The shared broker infrastructure is `LiteBus.Transport.IntegrationTesting` (`KafkaBrokerHost`, Redpanda default, `KafkaIngressTestSupport`).

### Untested (Kafka Docker; Covered Elsewhere)

- Duplicate MessageId idempotency (`LiteBus.Durable.IntegrationTests/Ingress/InMemory/`).
- Header edge cases (missing contract name, wrong version, invalid MessageId) (`Ingress/InMemory/`).
- `RequeueOnFailure = false` poison drain and explicit coverage of both requeue modes (`Ingress/InMemory/`, `Ingress/AwsSqs/`).
- `KafkaInboxIngressModuleBuilder.ConfigureHost` retry interval tuning under live broker load.
- Offset commit failure after accept with `ingress.ack_failed_after_accept` metric assertion.
- Azure- and AWS-style header edge-case parity inside the Kafka broker suite itself.

### Out-of-Scope

- GA production tier declaration (the current tier is Beta in the Feature Index).
- Full parity with AMQP ingress option surface on the Kafka builder.
- Kafka outbox ingress (outbox uses dispatch).

## References

- [Kafka transport](../../integrations/kafka.md)
- [Integration tests](../../testing/integration-tests.md) (Kafka harness notes)
- [Feature Index: Ingress](../../reference/feature-index.md)
