# Outbox Kafka Dispatch

## Header

- **ID**: `dispatch.outbox.kafka`
- **Name**: Outbox Kafka dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Kafka topics with contract headers via `UseKafkaDispatch`.

## What It Does

This extension registers `TransportOutboxDispatchModule<TTransportModule>` with `KafkaTransportModule`. The outbox processor produces records to the configured topic, with the key from the resolver, the topic metadata, or the contract name. The payload and headers follow the same mapping as other transport outbox adapters.

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

- The default hook failure policy is `CompleteDespiteHookFailure`.
- Delivery is at-least-once, so consumers must deduplicate using headers or business logic.
- The broker does not deduplicate republished outbox rows after a processor crash.

## Non-Goals

- It does not use Kafka idempotent producer settings as an outbox exactly-once guarantee.
- It does not consume from topics.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddKafkaTransport(new KafkaTransportOptions
    {
        BootstrapServers = "localhost:9092",
        ClientId = "orders-outbox-dispatch"
    });

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseKafkaDispatch(
            options =>
            {
                options.DefaultDestination = "orders.events";
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseKafkaDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers the outbox transport dispatcher, which requires the root Kafka transport |
| `TransportOutboxDispatchModule<TTransportModule>.DefaultHookFailurePolicy` | The policy defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes the outbox payload and headers to Kafka |

Route resolution tries the tenant strategy first, then the resolver, then the envelope `Topic`, then the contract name.

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | The topic destination, `messaging.kafka.message.key`, and message id attributes |
| `litebus.transport.circuit_breaker.open` / `failure_count` | Tag `litebus.transport.broker="kafka"` |
| `litebus.outbox.processor.published` / `failed` | The terminal outcomes after the Kafka publish attempt |
| `litebus.outbox.processor.dispatch_duration` | It includes producer send latency |

Failure paths increment the circuit breaker when the broker is unreachable (`KafkaDispatchFailureIntegrationTests`).

## References

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

- The after-dispatch hook failure path with `CompleteDespiteHookFailure`.
- The tenant route strategy branch and the custom resolver branch.
- Idempotent producer settings as an exactly-once guarantee, which is outside the dispatch guarantees.

### Out-of-Scope

- Kafka idempotent producer as outbox exactly-once guarantee
- Consuming from Kafka topics
