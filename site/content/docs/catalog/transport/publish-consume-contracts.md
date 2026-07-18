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
| `SubscriptionName` | `string?` | Named subscription for topic-based brokers |
| `MaxInFlightMessages` | `int` | Provider-neutral cap on concurrent LiteBus handler calls, default 32 |
| `PrefetchCount` | `int` | Native RabbitMQ or Azure Service Bus prefetch, default 0 |
| `ReceiveBatchSize` | `int` | Messages requested per SQS receive call, default 1 and valid from 1 through 10 |
| `MaxConcurrentCalls` | `int?` | Native Azure Service Bus callback concurrency |
| `DeclareDestination` | `bool` | Declare destination before consume |
| `DurableDestination` | `bool` | Durable destination declaration |
| `DestinationArguments` | `IReadOnlyDictionary<string, object?>?` | Broker-specific destination args |

`MaxInFlightMessages` has one meaning across every adapter: no more than that number of LiteBus delivery handlers execute concurrently for one subscription. Native broker controls remain separate because they regulate different resources. RabbitMQ prefetch limits unacknowledged deliveries, Azure prefetch fills a local cache while `MaxConcurrentCalls` controls processor callbacks, and SQS `ReceiveBatchSize` sets `ReceiveMessageRequest.MaxNumberOfMessages`.

The in-memory adapter separately bounds queued and in-flight deliveries with `InMemoryTransportOptions.DestinationCapacity`. This is publisher backpressure, not consumer callback concurrency. A full destination waits asynchronously with `BoundedChannelFullMode.Wait`, and publish cancellation stops that capacity wait without dropping an admitted delivery.

The limits match the current provider contracts:

- [RabbitMQ consumer prefetch](https://www.rabbitmq.com/docs/consumer-prefetch) applies to unacknowledged deliveries and treats zero as unlimited.
- [Azure Service Bus processor options](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusprocessoroptions?view=azure-dotnet) define prefetch and callback concurrency as separate properties.
- [AWS SDK for .NET v4 `ReceiveMessageRequest`](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TReceiveMessageRequest.html) permits 1 through 10 messages per receive call and defaults to 1.
- [Confluent.Kafka .NET consumer guidance](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html) describes one-record-at-a-time `Consume` calls over the client's background fetch queue, so LiteBus does not expose a false Kafka prefetch knob.
- [.NET channels guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) defines `BoundedChannelFullMode.Wait` as waiting for capacity instead of dropping a write or an existing item.

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
| `CreateBoundedHandler_WithThreeConcurrentCalls_ShouldAdmitConfiguredMaximum` | `LiteBus.Transport.UnitTests` |
| `InboxIngressOptions_ShouldPreserveSafetyAndNativeConsumerSettings` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

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
