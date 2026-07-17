# Outbox Kafka Dispatch

## Header

- **ID**: `dispatch.outbox.kafka`
- **Name**: Outbox Kafka dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Kafka topics with contract headers via `UseKafkaDispatch`.

## What It Does

Registers `TransportOutboxDispatchModule` with `KafkaTransportModule`. Outbox processor produces records to the configured topic with key from topic metadata, resolver, or contract name. Payload and headers follow the same mapping as other transport outbox adapters.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.Kafka` | Registration glue |
| `LiteBus.Outbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.Kafka` | Confluent adapter |

## Requires

- `dispatch.transport-core`
- `transport.kafka`
- `durable-core.outbox`

## Invariants

- Default hook failure policy: `CompleteDespiteHookFailure`.
- At-least-once: consumers must deduplicate using headers or business logic.
- No broker-level deduplication of republished outbox rows after processor crash.

## Non-Goals

- Does not use Kafka idempotent producer settings as outbox exactly-once guarantee.
- Does not consume from topics.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddOutboxModule(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseKafkaDispatch(
            options =>
            {
                options.DefaultDestination = "orders.events";
            },
            new KafkaTransportOptions
            {
                BootstrapServers = "localhost:9092",
                ClientId = "orders-outbox-dispatch"
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseKafkaDispatch(Action<TransportOutboxDispatcherOptions>, KafkaTransportOptions)` | Registers outbox transport dispatcher and Kafka transport module |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | Defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes outbox payload and headers to Kafka |

Route resolution: tenant strategy, then envelope `Topic`, then resolver, then contract name.

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Topic destination, `messaging.kafka.message.key`, and message id attributes |
| `litebus.transport.circuit_breaker.open` / `failure_count` | Tag `litebus.transport.broker="kafka"` |
| `litebus.outbox.processor.published` / `failed` | Terminal outcomes after Kafka publish attempt |
| `litebus.outbox.processor.dispatch_duration` | Includes producer send latency |

Failure paths increment circuit breaker when broker is unreachable (`KafkaDispatchFailureIntegrationTests`).

## Deep Docs

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Architecture.md: Kafka at-least-once semantics](../../architecture/README.md#kafka-at-least-once-semantics)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) (Kafka Theory case) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |

### Untested

- After-dispatch hook failure path with `CompleteDespiteHookFailure`.
- Tenant route strategy branch and custom resolver branch.
- Idempotent producer settings as an exactly-once guarantee, which is outside dispatch guarantees.

### Out-of-Scope

- Kafka idempotent producer as outbox exactly-once guarantee
- Consuming from Kafka topics
