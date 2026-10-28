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
| `ContentType` | `string` | Content type hint, default `application/json` |
| `ContentEncoding` | `string?` | Content encoding hint |
| `Persistent` | `bool` | Publish durability hint, default `true` |
| `Mandatory` | `bool` | Mandatory delivery hint, default `true` |
| `MessageId` | `string?` | Message id property |
| `CorrelationId` | `string?` | Correlation id property |
| `MessageGroupId` | `string?` | FIFO message group identifier for brokers with ordered groups |
| `DeduplicationKey` | `string?` | Deduplication key for brokers with content-based deduplication |

### Consumer Options Model

| Property | Type | Purpose |
| --- | --- | --- |
| `Destination` | `string` | Queue, topic, or destination name |
| `SubscriptionName` | `string?` | Named subscription for topic-based brokers |
| `MaxInFlightMessages` | `int` | Requested cap on concurrent LiteBus handler calls, default 32; enforced as 1 except on Azure Service Bus, where the lower of this and `MaxConcurrentCalls` applies |
| `PrefetchCount` | `int` | Native RabbitMQ or Azure Service Bus prefetch, default 0, which maps to the broker-native unlimited prefetch window on AMQP; set an explicit bound for large backlogs |
| `ReceiveBatchSize` | `int` | Messages requested per SQS receive call, default 1 and valid from 1 through 10 |
| `MaxConcurrentCalls` | `int?` | Native Azure Service Bus callback concurrency |
| `DeclareDestination` | `bool` | Declare destination before consume (default `true`; honored by AMQP only) |
| `DurableDestination` | `bool` | Durable destination declaration (default `true`; honored by AMQP only) |
| `Exclusive` | `bool` | Exclusive subscription for this connection (default `false`; honored by AMQP only) |
| `ConsumerTag` | `string?` | Client-assigned consumer tag (default `null`, meaning the broker assigns one; honored by AMQP only) |
| `DestinationArguments` | `IReadOnlyDictionary<string, object?>?` | Broker-specific destination args (honored by AMQP only) |

`MaxInFlightMessages` requests a cap on concurrent LiteBus delivery handlers for one subscription. The AMQP, Kafka, AWS SQS, and in-memory adapters enforce a bound of 1 and run one handler at a time, so raising `MaxInFlightMessages` alone does not parallelize them; the Azure Service Bus adapter enforces the lower of `MaxInFlightMessages` and `MaxConcurrentCalls`, and `MaxConcurrentCalls` defaults to the SDK value of 1, so both must be set to run handlers concurrently. Native broker controls remain separate because they regulate different resources. RabbitMQ prefetch limits unacknowledged deliveries, Azure prefetch fills a local cache while `MaxConcurrentCalls` controls processor callbacks, and SQS `ReceiveBatchSize` sets `ReceiveMessageRequest.MaxNumberOfMessages`.

Which adapter honors which option:

| Option | AMQP | Kafka | AWS SQS | Azure Service Bus | InMemory |
| --- | --- | --- | --- | --- | --- |
| `Destination` | Queue | Topic | Queue URL | Queue or topic | Destination name |
| `SubscriptionName` | Ignored | Ignored | Ignored | Topic subscription | Ignored |
| `PrefetchCount` | Consumer prefetch | Ignored | Ignored | Processor prefetch | Ignored |
| `ReceiveBatchSize` | Ignored | Ignored | Messages per receive (1-10) | Ignored | Ignored |
| `MaxConcurrentCalls` | Ignored | Ignored | Ignored | Processor callback concurrency | Ignored |
| `DeclareDestination`, `DurableDestination`, `Exclusive`, `ConsumerTag`, `DestinationArguments` | Queue declare and consume flags | Ignored | Ignored | Ignored | Ignored |

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
- Handler acknowledgement remains explicit via `TransportMessage` methods; the handler must call `AcceptAsync`, `DiscardAsync`, or `ReturnToQueueAsync` before returning.
- One broker module registration per process (`transport.single-broker-registration`).
- Transport publishers are singletons and consumers are transient in all five adapters, so a dispatched envelope reuses its publisher while each subscription holds its own consumer.
- `StartAsync` returns a task that completes when the consume loop has been initiated; startup and subscription failures surface through `WaitUntilStoppedAsync`, and transports that subscribe synchronously complete after the subscription is active.
- Calling `StartAsync` twice on one consumer instance throws `InvalidOperationException`.

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
