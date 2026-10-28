# Durable Dispatch

**ID:** `durable-core.durable-dispatch`
**Maturity:** GA (in-process and AMQP); Beta (Kafka, SQS, Azure Service Bus)

## Summary

Durable dispatch delivers leased inbox and outbox envelopes as side effects through in-process mediators or external message brokers.

## What It Does

Processors call `IInboxDispatcher.DispatchAsync` or `IOutboxDispatcher.DispatchAsync` inside per-message DI scopes. In-process inbox dispatch deserializes the payload, requires it to be an `ICommand`, and then calls `ICommandMediator.SendAsync`. In-process outbox dispatch calls `IEventMediator.PublishAsync`. Transport dispatchers publish through `ITransportPublisher` with contract headers and optional tenant routing. Each durable module registers exactly one dispatcher. Startup fails when the processor is enabled without a dispatcher.

## Public Surface

### Dispatcher Contracts

- **`IInboxDispatcher.DispatchAsync`**: A single-method interface that the inbox processor invokes inside a per-envelope scope. Throw on failure so the processor drives a retry or a dead letter.
- **`IOutboxDispatcher.DispatchAsync`**: A single-method interface. The outbox processor invokes it before the terminal persist on transport paths, which leaves an at-least-once publication window.

### In-Process Registration

| Extension | Package | Behavior |
| --- | --- | --- |
| `InboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` | Deserializes the payload, requires `ICommand`, and calls `ICommandMediator.SendAsync` |
| `OutboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` | Deserializes the payload and calls `IEventMediator.PublishAsync` |

In-process dispatch requires command or event module registration and handler discovery for the handled types.

### Transport Registration

| Extension | Package | Maturity |
| --- | --- | --- |
| `UseAmqpDispatch(configure)` | `LiteBus.Inbox.Dispatch.Amqp`, `LiteBus.Outbox.Dispatch.Amqp` | GA |
| `UseKafkaDispatch(configure)` | `*.Dispatch.Kafka` | Beta |
| `UseAwsSqsDispatch(configure)` | `*.Dispatch.AwsSqs` | Beta |
| `UseAzureServiceBusDispatch(configure)` | `*.Dispatch.AzureServiceBus` | Beta |
| `UseInMemoryDispatch(configure?)` | `*.Dispatch.InMemory` | Tests and samples |

Register the matching `Add*Transport(...)` once at the root. Each dispatch extension builds **`TransportInboxDispatcherOptions`** or **`TransportOutboxDispatcherOptions`**, registers a dispatcher child that requires the root transport module, and calls **`RegisterDispatcher`**. Registering a second dispatcher on the same builder throws **`DurableStorageConfigurationException`**.

### Transport Configuration

- **`TransportOutboxDispatcherOptions.ValidatePayloadBeforeDispatch`** (default `false`): A pre-dispatch deserialization gate. When the payload fails to deserialize, dispatch fails before publishing to the broker
- **Route precedence**: Routes resolve in this order: `ITenantRoutingStrategy` when one is registered, else the `ResolveRoute` delegate when set, else the envelope `Topic` (outbox only) when present, else the contract name. On the outbox axis, a strategy or delegate result of null or whitespace fails the dispatch with `LiteBusDispatchException`. On the inbox axis, a delegate result of null or whitespace fails with `InboxDispatchException`.
- **`TransportOutboxDispatcherOptions.TenantRoutingStrategy`**: Registers the strategy as the `ITenantRoutingStrategy` the outbox dispatcher resolves; the inbox dispatcher resolves an optional strategy from the container instead.
- **FIFO addressing**: The outbox dispatcher fills `TransportPublishRequest.MessageGroupId` (tenant id, or the contract name when unscoped) and `DeduplicationKey` (idempotency key, or the message id when absent) on every publish. The SQS adapter maps them only on queue URLs ending in `.fifo`.
- Inbox transport dispatch publishes the leased envelope (dual-hop topology)
- Outbox transport dispatch publishes before terminal persist

### In-Process Configuration

- **`InProcessOutboxDispatchOptions.RequireHandler`** (default `true`, set through `UseInProcessDispatch(dispatch => ...)`): When a dispatched message has no handler, the attempt fails, retries, and dead-letters. With `false`, the message is marked published without any handler having run. The inbox axis cannot fail this way because command mediation requires exactly one handler.
- **Per-handler envelopes**: In-process outbox dispatch publishes a targeted envelope to its recorded `Handler` alone. The pre, post, error, and completion observers still run (see [Per-Handler Delivery](outbox-enqueue.md#per-handler-delivery)).

### Extension Points

- Custom dispatchers implement **`IInboxDispatcher`** or **`IOutboxDispatcher`** and register through **`RegisterDispatcher(I*DispatcherModule)`**
- For wire metadata mapping, the internal **`OutboxTransportEnvelopeMapper`** and **`InboxTransportEnvelopeMapper`** in the shared dispatch packages own the header mapping. Custom dispatchers map metadata themselves

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InProcess` | Command inbox dispatcher |
| `LiteBus.Outbox.Dispatch.InProcess` | Event outbox dispatcher |
| `LiteBus.Inbox.Dispatch`, `LiteBus.Outbox.Dispatch` | Shared transport mapping |
| `LiteBus.Inbox.Dispatch.*`, `LiteBus.Outbox.Dispatch.*` | Broker glue |
| `LiteBus.Transport.*` | Broker clients |

## Requires

- The application registers exactly one dispatcher on each inbox or outbox module
- The dispatch or ingress extensions register the transport module when the consumer is not already present
- Handlers are registered for in-process dispatch, or broker topology exists for transport dispatch

## Invariants

- Dispatch exceptions drive a retry or a dead letter in the processor
- Inbox transport dispatch publishes the leased envelope (dual-hop topology)
- Outbox transport dispatch publishes before the terminal persist (at-least-once window)
- Custom dispatchers implement the single-method interfaces and throw on failure
- Route precedence is the tenant strategy, then `ResolveRoute`, then the envelope `Topic` (outbox), then the contract name
- A second dispatcher on one axis throws `DurableStorageConfigurationException`

## Non-Goals

- Inbox in-process dispatch to `IEventMediator` is a planned separate capability
- Webhook and gRPC outbox adapters are planned
- The module does not load-balance across multiple dispatchers

## Observability

### Processor Dispatch Duration

| Instrument | Constant | Axis | Kind |
| --- | --- | --- | --- |
| `litebus.inbox.processor.dispatch_duration` | `LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName` | Inbox | Histogram (ms) |
| `litebus.outbox.processor.dispatch_duration` | `LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName` | Outbox | Histogram (ms) |

- **When emitted**: The processor wraps every **`DispatchAsync`** call
- **Registration**: `AddLiteBusInboxMetrics()` or `AddLiteBusOutboxMetrics()`

### Processor Pass Counters

| Instrument | Axis | When incremented |
| --- | --- | --- |
| `litebus.inbox.processor.succeeded` | Inbox | Dispatch completes without an exception |
| `litebus.inbox.processor.failed` | Inbox | Dispatch throws, and the row is marked failed with retry visibility |
| `litebus.inbox.processor.dead_lettered` | Inbox | Max attempts or the hook policy moves the row to dead letter |
| `litebus.outbox.processor.published` | Outbox | Transport or in-process publish succeeds |
| `litebus.outbox.processor.failed` | Outbox | Dispatch throws |
| `litebus.outbox.processor.dead_lettered` | Outbox | The retry policy is exhausted |

### Transport Send Tracing

- **Activity**: `send {destination}` on activity source `LiteBus.Transport`
- **When started**: The concrete broker publisher starts it around its SDK send call
- **Registration**: `AddLiteBusTransportInstrumentation()`

### Circuit Breaker (Transport Dispatch)

| Instrument | Constant | Kind |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | Observable gauge |
| `litebus.transport.circuit_breaker.failure_count` | `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | Observable gauge |

- **Tag**: `litebus.transport.broker` (`BrokerTagName`) per broker module
- **When read**: An open breaker blocks publishing, and a dispatch failure increments the processor **`failed`** counter
- **Registration**: `AddLiteBusTransportMetrics()` or broker aliases such as `AddLiteBusAmqpMetrics()`

### Lease Interaction During Dispatch

| Instrument | Constant | When incremented |
| --- | --- | --- |
| `litebus.inbox.processor.lease_lost` | `ProcessorLeaseLostInstrumentName` | Lease renewal fails during inbox dispatch |
| `litebus.outbox.processor.lease_lost` | `ProcessorLeaseLostInstrumentName` | Lease renewal fails during outbox dispatch |

## References

- [Inbox](../../reliable-messaging/inbox.md) (dispatch table)
- [Outbox](../../reliable-messaging/outbox.md) (dispatch table)
- [Outbox AMQP dispatch](../../integrations/outbox-amqp-dispatch.md)
- [Architecture](../../architecture/README.md) (dispatch axis)
- Transport guides: [AMQP](../../integrations/amqp.md), [Kafka](../../integrations/kafka.md), etc.

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`

### Covered Use Cases

#### `InProcessInboxDispatcherTests.DispatchAsync_ShouldExecuteCommandThroughMediator`

- **Use case**: When in-process inbox dispatch runs, the command executes through the command mediator
- **Test kind**: Unit
- **Description**: The **`UseInProcessDispatch`** wiring with a registered handler
- **Behavior**: **`IInboxDispatcher.DispatchAsync`** runs on a leased envelope
- **Expected outcome**: **`ICommandMediator.SendAsync`** runs, and the handler runs
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessOutboxDispatcherTests.InProcessOutboxDispatcher_ShouldPublishPocoEvent`

- **Use case**: When in-process outbox dispatch runs, the event publishes through the event mediator
- **Test kind**: Unit
- **Description**: The in-process outbox dispatcher with a POCO event
- **Behavior**: **`IOutboxDispatcher.DispatchAsync`** runs
- **Expected outcome**: The event handler runs
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessOutboxDispatcherTests.InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings`

- **Use case**: When outbox dispatch carries trace metadata, it is copied into mediation settings
- **Test kind**: Unit
- **Description**: Trace metadata on the envelope
- **Behavior**: In-process outbox dispatch runs
- **Expected outcome**: The trace fields are present in **`EventMediationSettings`**
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessInboxDispatcherTests.AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow`

- **Use case**: When a second inbox dispatcher is registered on the same builder, compose fails
- **Test kind**: Unit
- **Description**: The duplicate dispatcher registration guard
- **Behavior**: Two dispatcher registrations against one inbox module
- **Expected outcome**: **`LiteBusConfigurationException`** naming `IInboxDispatcher`
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`). The builder-level duplicate throws the derived `DurableStorageConfigurationException`

#### `TransportInboxDispatcherTests.DispatchAsync_ShouldPublishEnvelopeThroughTransport`

- **Use case**: When transport inbox dispatch runs, the leased envelope publishes through **`ITransportPublisher`**
- **Test kind**: Unit
- **Description**: The transport inbox dispatcher with a test transport
- **Behavior**: **`DispatchAsync`** runs on the transport path
- **Expected outcome**: The envelope is published to the transport
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/`)

#### `AmqpInboxDispatcherIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue`

- **Use case**: When the inbox processor dispatches through AMQP, the leased envelope appears on the queue
- **Test kind**: Integration
- **Description**: A real AMQP broker
- **Behavior**: A processor pass with AMQP inbox dispatch
- **Expected outcome**: The message reaches the AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`)

#### `PostgreSqlAmqpInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopeCompleted`

- **Use case**: When a PostgreSQL inbox uses AMQP dispatch, the envelope publishes and the row completes in the database
- **Test kind**: Integration
- **Description**: A dual-hop topology with a PostgreSQL store
- **Behavior**: A full processor pass
- **Expected outcome**: The message reaches the queue, and the row completes in PostgreSQL
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue`

- **Use case**: When the outbox processor dispatches through AMQP, the envelope publishes to the broker
- **Test kind**: Integration
- **Description**: The AMQP outbox dispatch path
- **Behavior**: A processor pass with the transport outbox dispatcher
- **Expected outcome**: The event is published to the AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoutingKey`

- **Use case**: When the outbox publication target omits the topic, dispatch uses the contract name as the routing key
- **Test kind**: Integration
- **Description**: The routing fallback on AMQP outbox dispatch
- **Behavior**: Enqueue without explicit topic metadata
- **Expected outcome**: The contract name is used as the route
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `InMemoryInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination`

- **Use case**: When inbox dispatch uses the in-memory transport, the leased envelope delivers to the configured destination
- **Test kind**: Integration
- **Description**: Test transport inbox dispatch
- **Behavior**: A processor pass with in-memory dispatch
- **Expected outcome**: The message reaches the in-memory destination
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`)

#### `InMemoryOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination`

- **Use case**: When outbox dispatch uses the in-memory transport, the event delivers to the configured destination
- **Test kind**: Integration
- **Description**: Test transport outbox dispatch
- **Behavior**: A processor pass with in-memory dispatch
- **Expected outcome**: The event is delivered
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`)

#### `KafkaInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic`

- **Use case**: When inbox dispatch uses Kafka, the leased envelope publishes to the topic
- **Test kind**: Integration
- **Description**: Kafka inbox dispatch (beta)
- **Behavior**: A processor pass with the Kafka dispatcher
- **Expected outcome**: The message reaches the Kafka topic
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`)

#### `KafkaOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic`

- **Use case**: When outbox dispatch uses Kafka, the event publishes to the topic
- **Test kind**: Integration
- **Description**: Kafka outbox dispatch (beta)
- **Behavior**: A processor pass with the Kafka dispatcher
- **Expected outcome**: The event reaches the Kafka topic
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`)

#### `AwsSqsInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue`

- **Use case**: When inbox dispatch uses SQS, the leased envelope publishes to the queue
- **Test kind**: Integration
- **Description**: AWS SQS inbox dispatch (beta)
- **Behavior**: A processor pass with the SQS dispatcher
- **Expected outcome**: The message reaches the SQS queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`)

#### `AwsSqsOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue`

- **Use case**: When outbox dispatch uses SQS, the event publishes to the queue
- **Test kind**: Integration
- **Description**: AWS SQS outbox dispatch (beta)
- **Behavior**: A processor pass with the SQS dispatcher
- **Expected outcome**: The event reaches the SQS queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `AzureServiceBusInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue`

- **Use case**: When inbox dispatch uses Azure Service Bus, the leased envelope publishes to the queue
- **Test kind**: Integration
- **Description**: Azure Service Bus inbox dispatch (beta)
- **Behavior**: A processor pass with the Azure dispatcher
- **Expected outcome**: The message reaches the Service Bus queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`)

#### `AzureServiceBusOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue`

- **Use case**: When outbox dispatch uses Azure Service Bus, the event publishes to the queue
- **Test kind**: Integration
- **Description**: Azure Service Bus outbox dispatch (beta)
- **Behavior**: A processor pass with the Azure dispatcher
- **Expected outcome**: The event reaches the Service Bus queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`)

#### `AwsSqsDispatchFailureIntegrationTests.ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter`

- **Use case**: When dispatch fails because the broker is unreachable, the row is marked failed with retry visibility
- **Test kind**: Integration
- **Description**: An unreachable broker during SQS dispatch
- **Behavior**: A processor pass with a broker failure
- **Expected outcome**: Failed status with a future **`visible_after`**
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `AwsSqsDispatchFailureIntegrationTests.ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish`

- **Use case**: When the transport circuit breaker is open, dispatch does not attempt a publish
- **Test kind**: Integration
- **Description**: An open circuit breaker during outbox dispatch
- **Behavior**: A processor pass with an open breaker
- **Expected outcome**: No publish attempt
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `BrokerDispatchIngressRegistrationTests.InboxDispatchExtensions_ShouldRegisterTransportDispatcher`

- **Use case**: When broker dispatch extensions are used on the inbox builder, the transport dispatcher registers
- **Test kind**: Integration
- **Description**: A module builder registration theory over brokers
- **Behavior**: **`Use*Dispatch`** on the inbox module for InMemory, AMQP, Azure, SQS, and Kafka
- **Expected outcome**: **`IInboxDispatcher`** is registered
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Registration/`)

#### `OutboxTransportEnvelopeMapperTests.BuildHeaders_ShouldMapAllMetadataFields`

- **Use case**: When outbox transport maps an envelope to wire headers, all metadata fields appear on the message
- **Test kind**: Unit
- **Description**: The transport header mapper for the outbox
- **Behavior**: **`BuildHeaders`** runs on an outbox envelope
- **Expected outcome**: The contract, trace, tenant, and routing headers are populated
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_enabled_should_throw_before_publish`

- **Use case**: When validate-payload-before-dispatch is enabled, a deserialization failure throws before the broker publish
- **Test kind**: Unit
- **Description**: The **`ValidatePayloadBeforeDispatch`** gate
- **Behavior**: Dispatch with invalid payload bytes
- **Expected outcome**: An exception before the transport publish
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `TransportOutboxDispatcherResolveRouteTests.DispatchAsync_WithResolveRouteAndTopic_ShouldUseResolveRoute`

- **Use case**: When both `ResolveRoute` and a topic are set, the delegate wins
- **Test kind**: Unit
- **Description**: Outbox route precedence with topic metadata present
- **Behavior**: **`DispatchAsync`** runs with `ResolveRoute` configured and `Topic` on the envelope
- **Expected outcome**: The published route comes from the delegate, not the topic
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`). Companion tests cover the topic fallback and the empty-result `LiteBusDispatchException`

#### `TransportOutboxDispatcherTests.DispatchAsync_WithTenantRoutingStrategy_ShouldUseResolvedRoute`

- **Use case**: When a tenant routing strategy is registered, its route wins over every fallback
- **Test kind**: Unit
- **Description**: The strategy receives the envelope tenant, contract, and topic metadata
- **Behavior**: **`DispatchAsync`** runs with a recording `ITenantRoutingStrategy`
- **Expected outcome**: The published route is the strategy return value
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`). The inbox twin is `TransportInboxDispatcherTests.DispatchAsync_WithTenantRoutingStrategy_ShouldUseResolvedRoute`

#### `TransportOutboxDispatcherFifoFieldsTests.DispatchAsync_WhenIdempotencyKeyAndTenantIdAreSet_ShouldMapFifoFields`

- **Use case**: When the envelope carries an idempotency key and tenant id, FIFO addressing follows them
- **Test kind**: Unit
- **Description**: An outbox transport publish with keyed idempotency and tenant scope
- **Behavior**: **`DispatchAsync`** runs on the transport path
- **Expected outcome**: `DeduplicationKey` is the idempotency key, and `MessageGroupId` is the tenant id
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`). Companion tests cover the message-id and contract-name fallbacks

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Inbox in-process dispatch to events | No (planned) | N/A | N/A | N/A |
| Webhook/gRPC outbox adapters | No (planned) | N/A | N/A | N/A |
| Multi-dispatcher within one module | No (by design) | N/A | N/A | N/A |

### Out-of-Scope Use Cases

- Inbox in-process event replay through `IEventMediator`
- Webhook and gRPC publication adapters
- Load-balanced multi-dispatcher registration
