# Kafka Transport

- **ID**: `transport.kafka`
- **Name**: Kafka transport
- **Maturity**: GA
- **Summary**: Kafka adapter built on `Confluent.Kafka` with manual offset commit and seek-based redelivery.

## What It Does

`KafkaTransportModule` registers `KafkaPublisher` and `KafkaConsumer` behind transport contracts. Publish maps `Destination` to topic and `Route` to record key. Consume disables auto-commit and only commits on `AcceptAsync`. The publisher is a singleton over one shared producer that is flushed once when the container is disposed; consumers are transient. Resolve `ITransportPublisher` to publish; the module does not register the bare Confluent producer.

`KafkaConsumer.StartAsync` starts the consume loop on the thread pool and returns, because the Confluent `Consume` call blocks until a record arrives. When handlers call `ReturnToQueueAsync`, the adapter seeks to the consumed offset and applies configurable exponential backoff via `KafkaSeekBackoff`. Each consumer owns a dedicated `IConsumer<string, byte[]>`: the client holds single-subscription state and is not thread-safe, so the module registers it as transient and a client is never shared across consumers. `StopAsync` closes the owned client for a prompt group leave, and `DisposeAsync` disposes it.

## Public Surface

### Registration and Runtime Types

| API | Role |
| --- | --- |
| `KafkaTransportModule` | Registers Kafka publisher and consumer |
| `KafkaPublisher.PublishAsync` | Produces one Kafka record |
| `KafkaConsumer.StartAsync` | Runs consume loop with manual commit |
| `KafkaMessageMapper` | Maps `TransportPublishRequest` and consumed records (internal) |
| `KafkaSeekBackoff` | Tracks per-offset retry delay (internal) |
| `KafkaConnectivityDiagnosticCheck` | Describes the cluster for host readiness |
| `LiteBusTransportKafkaTelemetry.MeterName` | Reserved Kafka meter name |

### Options

| Property | Default | Purpose |
| --- | --- | --- |
| `BootstrapServers` | required | Kafka bootstrap server list |
| `ClientId` | `null` | Producer and consumer client id |
| `ConsumerGroupId` | `litebus-transport` | Kafka consumer group |
| `MessageTimeoutMs` | `null` | Producer message timeout; `null` leaves the Confluent default |
| `MessageSendMaxRetries` | `null` | Producer send retries; `null` leaves the Confluent default, a set value must be positive |
| `SocketTimeoutMs` | `null` | Producer socket timeout; `null` leaves the Confluent default, a set value must be positive |
| `ConnectivityCheckTimeout` | `5s` | Cluster description request timeout |
| `SeekFailureBackoffInitial` | `250ms` | Initial seek retry delay |
| `SeekFailureBackoffMax` | `30s` | Max seek retry delay |
| `SeekFailureBackoffMultiplier` | `2.0` | Exponential backoff multiplier |

### Ack Mapping

| Transport call | Kafka action |
| --- | --- |
| `AcceptAsync` | Commit consumed offset |
| `DiscardAsync` | Commit consumed offset |
| `ReturnToQueueAsync` | Seek to consumed offset (do not commit) |

## Packages

- `LiteBus.Transport.Kafka`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`

## Invariants

- Consumer auto-commit is disabled.
- Offset progression happens only through explicit ack delegates.
- Ordering guarantee is partition-scoped, not topic-scoped.
- Each producer option maps only its own Confluent field; unset options leave Confluent defaults in place.
- A transient consume error backs off one second and resumes polling; only a fatal client error faults the loop.
- A requeue seek that fails transiently retries with the seek backoff schedule until it succeeds or shutdown cancels it; only a fatal error escapes to fault the loop, and the nack honors its `CancellationToken`.
- `StopAsync` completes without throwing when the consume loop faulted; the terminal failure propagates from `WaitUntilStoppedAsync` before and after `StopAsync`.

## Non-Goals

- Exactly-once transaction support.
- Topic provisioning and ACL management.
- Multi-broker compose in one process.

## Observability

### Metrics

| Item | Value |
| --- | --- |
| Shared meter | `LiteBus.Transport` |
| Broker tag | `litebus.transport.broker="kafka"` |
| Shared gauges | `litebus.transport.circuit_breaker.open`, `litebus.transport.circuit_breaker.failure_count` |
| Reserved adapter meter | `LiteBus.Transport.Kafka` (`LiteBusTransportKafkaTelemetry.MeterName`) |
| OpenTelemetry registration | `AddLiteBusTransportMetrics()` |

### Tracing

- Activity source `LiteBus.Transport` with `send {destination}` and `process {destination}` spans.
- Consume spans include destination, key, message id, and correlation id when present.

### Diagnostics

- Diagnostic check id: `transport.kafka.connectivity`
- `DescribeClusterAsync` must return at least one broker within `ConnectivityCheckTimeout`.
- Provider exception text is not returned. Caller cancellation propagates to the host runner.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_ShouldRegisterTransportServices` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Build_ShouldRegisterPublisherSingletonAndConsumerTransient` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `BuildProducerConfig_WithAllOptionsSet_ShouldMapEachFieldFromItsOwnOption` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `BuildProducerConfig_WithNullOptions_ShouldLeaveConfluentDefaults` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Constructor_WithNonPositiveSendRetries_ShouldThrow` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Constructor_WithNonPositiveSocketTimeout_ShouldThrow` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `SharedHeaders_ShouldUseCanonicalWireNames` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `ToKafkaMessage_ShouldWriteContentTypeUnderSharedHeaderName` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `CheckAsync_WhenClusterIsUnavailable_ShouldReturnUnhealthy` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `CheckAsync_WhenCallerCancels_ShouldPropagateCancellation` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `ToKafkaMessage_ShouldMapKeyBodyAndHeaders` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `ToTransportMessage_ShouldExposeCommitDelegate` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `ToTransportMessage_ReturnToQueueAsync_ShouldSeekToConsumedOffset` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `RecordSeek_repeatedFailures_ShouldIncreaseBackoff` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand` readiness assertion | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `TransientAcceptFailure_ShouldRedeliverSameOffsetWithoutRestart` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |

### Untested

- Raw `KafkaPublisher` and `KafkaConsumer` against a live broker outside durable wrappers.
- Exported gauge assertions for broker tag `kafka`.
- Broker integration assertions for exported Kafka activities.

### Out-of-Scope

- Exactly-once Kafka transactions.
- Automatic topic creation policy for production.

## Deep Docs

- [Kafka-Transport.md](../../integrations/kafka.md)
- [Architecture.md](../../architecture/README.md)
- [manual-acknowledgement.md](manual-acknowledgement.md)
