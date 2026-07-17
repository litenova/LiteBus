# Publish and Consume Contracts

- **ID**: `transport.publish-consume-contracts`
- **Name**: Publish and consume contracts
- **Maturity**: GA
- **Summary**: Broker-neutral API contract for publishing messages and running consumer loops.

## What It Does

Transport adapters implement:

- `ITransportPublisher` for outbound publish via `TransportPublishRequest`
- `IMessageConsumer` for inbound consume loops via `TransportConsumerOptions` and `TransportMessage` handlers

These abstractions isolate dispatch and ingress from broker SDK APIs.

## Public Surface

### Core Contracts

| Type | Member | Role |
| --- | --- | --- |
| `ITransportPublisher` | `PublishAsync(TransportPublishRequest, CancellationToken)` | Publish one message |
| `IMessageConsumer` | `StartAsync(TransportConsumerOptions, handler, CancellationToken)` | Start consume loop |
| `IMessageConsumer` | `StopAsync(CancellationToken)` | Request stop |
| `IMessageConsumer` | `WaitUntilStoppedAsync(CancellationToken)` | Await full shutdown |
| `IMessageConsumer` | `IAsyncDisposable` | Async release |

### Publish Request Model

| Property | Type | Purpose |
| --- | --- | --- |
| `Destination` | `string` | Broker destination target |
| `Route` | `string?` | Broker route key or topic hint |
| `Body` | `ReadOnlyMemory<byte>` | Payload bytes |
| `Headers` | `IReadOnlyDictionary<string, object?>?` | Application headers |
| `ContentType` | `string?` | Content type hint |
| `Persistent` | `bool` | Publish durability hint |
| `Mandatory` | `bool` | Mandatory delivery hint |
| `MessageId` | `string?` | Message id property |
| `CorrelationId` | `string?` | Correlation id property |

### Consumer Options Model

| Property | Type | Purpose |
| --- | --- | --- |
| `Destination` | `string` | Queue, topic, or destination name |
| `PrefetchCount` | `ushort` | In-flight delivery limit |
| `DeclareDestination` | `bool` | Declare destination before consume |
| `DurableDestination` | `bool` | Durable destination declaration |
| `DestinationArguments` | `IReadOnlyDictionary<string, object?>?` | Broker-specific destination args |

## Packages

- `LiteBus.Transport.Abstractions`

## Requires

- None

## Invariants

- Contracts stay broker-neutral and do not reference SDK types.
- Handler acknowledgement remains explicit via `TransportMessage` methods.
- One broker module registration per process (`transport.single-broker-registration`).

## Non-Goals

- Durable inbox or outbox persistence semantics.
- Automatic message deserialization and handler binding.
- Multi-broker transaction support.

## Observability

- Tracing uses shared source and span names (`transport.tracing`).
- Circuit-breaker metrics attach at adapter modules (`transport.metrics`).

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `CanonicalHeaders_ShouldUseStableWireNames` | `LiteBus.Transport.UnitTests` |
| `PublishAsync_ThenConsume_AcknowledgesMessage` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `StopAsync_AfterStart_PreventsFurtherDeliveries` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAndConsume_ShouldDeliverMessageToConsumer` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| `PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |

### Untested

- `WaitUntilStoppedAsync` semantics across all adapters.
- `IAsyncDisposable` behavior after partial startup failure.
- Adapter-level behavior for `Mandatory` and `Persistent` hints.

### Out-of-Scope

- Durable storage semantics.
- Handler registration and contract resolution policy.

## Deep Docs

- [manual-acknowledgement.md](manual-acknowledgement.md)
- [Architecture.md](../../architecture/README.md)
