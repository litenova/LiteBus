# Durable Dispatch

**ID:** `durable-core.durable-dispatch`
**Maturity:** GA (in-process and AMQP); Beta (Kafka, SQS, Azure Service Bus)

## Summary

Turn leased inbox and outbox envelopes into side effects through in-process mediators or external message brokers.

## What It Does

Processors call `IInboxDispatcher.DispatchAsync` or `IOutboxDispatcher.DispatchAsync` inside per-message DI scopes. In-process inbox dispatch deserializes and requires `ICommand`, then `ICommandMediator.SendAsync`. In-process outbox dispatch calls `IEventMediator.PublishAsync`. Transport dispatchers publish through `ITransportPublisher` with contract headers and optional tenant routing. Exactly one dispatcher registers per durable module; startup fails if processor enabled without dispatcher.

## Public Surface

### Dispatcher Contracts

- **`IInboxDispatcher.DispatchAsync`**: Single-method interface; inbox processor invokes inside per-envelope scope. Throw on failure to drive retry or dead letter.
- **`IOutboxDispatcher.DispatchAsync`**: Single-method interface; outbox processor invokes before terminal persist on transport paths (at-least-once publication window).

### In-Process Registration

| Extension | Package | Behavior |
| --- | --- | --- |
| `InboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` | Deserializes payload, requires `ICommand`, calls `ICommandMediator.SendAsync` |
| `OutboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` | Deserializes payload, calls `IEventMediator.PublishAsync` |

Requires command or event module registration and handler discovery for handled types.

### Transport Registration

| Extension | Package | Maturity |
| --- | --- | --- |
| `UseAmqpDispatch(configure)` | `LiteBus.Inbox.Dispatch.Amqp`, `LiteBus.Outbox.Dispatch.Amqp` | GA |
| `UseKafkaDispatch(configure)` | `*.Dispatch.Kafka` | Beta |
| `UseAwsSqsDispatch(configure)` | `*.Dispatch.AwsSqs` | Beta |
| `UseAzureServiceBusDispatch(configure)` | `*.Dispatch.AzureServiceBus` | Beta |
| `UseInMemoryDispatch(configure?)` | `*.Dispatch.InMemory` | Tests and samples |

Register the matching `Add*Transport(...)` once at the root. Each dispatch extension builds **`TransportInboxDispatcherOptions`** or **`TransportOutboxDispatcherOptions`**, registers a dispatcher child that requires the root transport module, and calls **`RegisterDispatcher`**. Second dispatcher registration on the same builder throws **`LiteBusConfigurationException`**.

### Transport Configuration

- **`TransportOutboxDispatcherOptions.ValidatePayloadBeforeDispatch`**: Pre-dispatch deserialization gate; fail before broker publish when payload cannot deserialize
- **`ITenantRoutingStrategy`**: Resolves broker destination from tenant metadata on transport adapters
- Inbox transport dispatch publishes the leased envelope (dual-hop topology)
- Outbox transport dispatch publishes before terminal persist

### Extension Points

- Custom dispatchers implement **`IInboxDispatcher`** or **`IOutboxDispatcher`** and register through **`RegisterDispatcher(I*DispatcherModule)`**
- Wire metadata mapping: **`OutboxTransportEnvelopeMapper`** / inbox transport mappers (shared dispatch packages)

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InProcess` | Command inbox dispatcher |
| `LiteBus.Outbox.Dispatch.InProcess` | Event outbox dispatcher |
| `LiteBus.Inbox.Dispatch`, `LiteBus.Outbox.Dispatch` | Shared transport mapping |
| `LiteBus.Inbox.Dispatch.*`, `LiteBus.Outbox.Dispatch.*` | Broker glue |
| `LiteBus.Transport.*` | Broker clients |

## Requires

- Exactly one dispatcher registration per inbox/outbox module
- Transport module registered by dispatch/ingress extensions when consumer not already present
- Handlers (in-process) or broker topology (transport)

## Invariants

- Dispatch exceptions drive retry/dead-letter in processor
- Inbox transport dispatch publishes leased envelope (dual-hop topology)
- Outbox transport dispatch publishes before terminal persist (at-least-once window)
- Custom dispatchers implement single-method interfaces; throw on failure
- `ITenantRoutingStrategy` on transport adapters resolves destinations from tenant metadata

## Non-Goals

- Inbox in-process dispatch to `IEventMediator` (planned separate capability)
- Webhook/gRPC outbox adapters (planned)
- Multi-dispatcher load balancing within one module

## Observability

### Processor Dispatch Duration

| Instrument | Constant | Axis | Kind |
| --- | --- | --- | --- |
| `litebus.inbox.processor.dispatch_duration` | `LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName` | Inbox | Histogram (ms) |
| `litebus.outbox.processor.dispatch_duration` | `LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName` | Outbox | Histogram (ms) |

- **When emitted**: Processor wraps every **`DispatchAsync`** call
- **Registration**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`

### Processor Pass Counters

| Instrument | Axis | When incremented |
| --- | --- | --- |
| `litebus.inbox.processor.succeeded` | Inbox | Dispatch completes without exception |
| `litebus.inbox.processor.failed` | Inbox | Dispatch throws; row marked failed with retry visibility |
| `litebus.inbox.processor.dead_lettered` | Inbox | Max attempts or hook policy moves row to dead letter |
| `litebus.outbox.processor.published` | Outbox | Transport or in-process publish succeeds |
| `litebus.outbox.processor.failed` | Outbox | Dispatch throws |
| `litebus.outbox.processor.dead_lettered` | Outbox | Retry exhausted |

### Transport Send Tracing

- **Activity**: `send {destination}` on activity source `LiteBus.Transport`
- **When started**: The concrete broker publisher around its SDK send call
- **Registration**: `AddLiteBusTransportInstrumentation()`

### Circuit Breaker (Transport Dispatch)

| Instrument | Constant | Kind |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | Observable gauge |
| `litebus.transport.circuit_breaker.failure_count` | `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | Observable gauge |

- **Tag**: `litebus.transport.broker` (`BrokerTagName`) per broker module
- **When read**: Open breaker blocks publish; dispatch failure increments processor **`failed`** counter
- **Registration**: `AddLiteBusTransportMetrics()` or broker aliases such as `AddLiteBusAmqpMetrics()`

### Lease Interaction During Dispatch

| Instrument | Constant | When incremented |
| --- | --- | --- |
| `litebus.inbox.processor.lease_lost` | `ProcessorLeaseLostInstrumentName` | Lease renewal fails during inbox dispatch |
| `litebus.outbox.processor.lease_lost` | `ProcessorLeaseLostInstrumentName` | Lease renewal fails during outbox dispatch |

## Deep Docs

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
- **Description**: **`UseInProcessDispatch`** wiring with registered handler
- **Behavior**: **`IInboxDispatcher.DispatchAsync`** on leased envelope
- **Expected outcome**: **`ICommandMediator.SendAsync`** invoked; handler runs
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessOutboxDispatcherTests.InProcessOutboxDispatcher_ShouldPublishPocoEvent`

- **Use case**: When in-process outbox dispatch runs, the event publishes through the event mediator
- **Test kind**: Unit
- **Description**: In-process outbox dispatcher with POCO event
- **Behavior**: **`IOutboxDispatcher.DispatchAsync`**
- **Expected outcome**: Event handler invoked
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessOutboxDispatcherTests.InProcessOutboxDispatcher_ShouldCopyTraceMetadataIntoMediationSettings`

- **Use case**: When outbox dispatch carries trace metadata, it is copied into mediation settings
- **Test kind**: Unit
- **Description**: Trace metadata on envelope
- **Behavior**: In-process outbox dispatch
- **Expected outcome**: Trace fields present in **`EventMediationSettings`**
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessInboxDispatcherTests.AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow`

- **Use case**: When a second inbox dispatcher is registered on the same builder, compose fails
- **Test kind**: Unit
- **Description**: Duplicate dispatcher registration guard
- **Behavior**: Two dispatcher **`Use*Dispatch`** calls on inbox builder
- **Expected outcome**: **`LiteBusConfigurationException`**
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`)

#### `TransportInboxDispatcherTests.DispatchAsync_ShouldPublishEnvelopeThroughTransport`

- **Use case**: When transport inbox dispatch runs, the leased envelope publishes through **`ITransportPublisher`**
- **Test kind**: Unit
- **Description**: Transport inbox dispatcher with test transport
- **Behavior**: **`DispatchAsync`** on transport path
- **Expected outcome**: Envelope published to transport
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/`)

#### `AmqpInboxDispatcherIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue`

- **Use case**: When inbox processor dispatches through AMQP, the leased envelope appears on the queue
- **Test kind**: Integration
- **Description**: Real AMQP broker
- **Behavior**: Processor pass with AMQP inbox dispatch
- **Expected outcome**: Message on AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`)

#### `PostgreSqlAmqpInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopeCompleted`

- **Use case**: When PostgreSQL inbox uses AMQP dispatch, the envelope publishes and the row completes in the database
- **Test kind**: Integration
- **Description**: Dual-hop topology with PostgreSQL store
- **Behavior**: Full processor pass
- **Expected outcome**: Message on queue; row completed in PostgreSQL
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue`

- **Use case**: When outbox processor dispatches through AMQP, the envelope publishes to the broker
- **Test kind**: Integration
- **Description**: AMQP outbox dispatch path
- **Behavior**: Processor pass with transport outbox dispatcher
- **Expected outcome**: Event published to AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoutingKey`

- **Use case**: When outbox publication target omits topic, dispatch uses contract name as routing key
- **Test kind**: Integration
- **Description**: Routing fallback on AMQP outbox dispatch
- **Behavior**: Enqueue without explicit topic metadata
- **Expected outcome**: Contract name used as route
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `InMemoryInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination`

- **Use case**: When inbox dispatch uses in-memory transport, the leased envelope delivers to the configured destination
- **Test kind**: Integration
- **Description**: Test transport inbox dispatch
- **Behavior**: Processor pass with in-memory dispatch
- **Expected outcome**: Message delivered to in-memory destination
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`)

#### `InMemoryOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination`

- **Use case**: When outbox dispatch uses in-memory transport, the event delivers to the configured destination
- **Test kind**: Integration
- **Description**: Test transport outbox dispatch
- **Behavior**: Processor pass with in-memory dispatch
- **Expected outcome**: Event delivered
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`)

#### `KafkaInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic`

- **Use case**: When inbox dispatch uses Kafka, the leased envelope publishes to the topic
- **Test kind**: Integration
- **Description**: Kafka inbox dispatch (beta)
- **Behavior**: Processor pass with Kafka dispatcher
- **Expected outcome**: Message on Kafka topic
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`)

#### `KafkaOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic`

- **Use case**: When outbox dispatch uses Kafka, the event publishes to the topic
- **Test kind**: Integration
- **Description**: Kafka outbox dispatch (beta)
- **Behavior**: Processor pass with Kafka dispatcher
- **Expected outcome**: Event on Kafka topic
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`)

#### `AwsSqsInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue`

- **Use case**: When inbox dispatch uses SQS, the leased envelope publishes to the queue
- **Test kind**: Integration
- **Description**: AWS SQS inbox dispatch (beta)
- **Behavior**: Processor pass with SQS dispatcher
- **Expected outcome**: Message on SQS queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`)

#### `AwsSqsOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue`

- **Use case**: When outbox dispatch uses SQS, the event publishes to the queue
- **Test kind**: Integration
- **Description**: AWS SQS outbox dispatch (beta)
- **Behavior**: Processor pass with SQS dispatcher
- **Expected outcome**: Event on SQS queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `AzureServiceBusInboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue`

- **Use case**: When inbox dispatch uses Azure Service Bus, the leased envelope publishes to the queue
- **Test kind**: Integration
- **Description**: Azure Service Bus inbox dispatch (beta)
- **Behavior**: Processor pass with Azure dispatcher
- **Expected outcome**: Message on Service Bus queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`)

#### `AzureServiceBusOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue`

- **Use case**: When outbox dispatch uses Azure Service Bus, the event publishes to the queue
- **Test kind**: Integration
- **Description**: Azure Service Bus outbox dispatch (beta)
- **Behavior**: Processor pass with Azure dispatcher
- **Expected outcome**: Event on Service Bus queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`)

#### `AwsSqsDispatchFailureIntegrationTests.ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter`

- **Use case**: When dispatch fails because the broker is unreachable, the row is marked failed with retry visibility
- **Test kind**: Integration
- **Description**: Unreachable broker during SQS dispatch
- **Behavior**: Processor pass with broker failure
- **Expected outcome**: Failed status with future **`visible_after`**
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `AwsSqsDispatchFailureIntegrationTests.ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish`

- **Use case**: When the transport circuit breaker is open, dispatch does not attempt publish
- **Test kind**: Integration
- **Description**: Open circuit breaker during outbox dispatch
- **Behavior**: Processor pass with open breaker
- **Expected outcome**: No publish attempt
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`)

#### `BrokerDispatchIngressRegistrationIntegrationTests.InboxDispatchExtensions_ShouldRegisterTransportDispatcher`

- **Use case**: When broker dispatch extensions are used on the inbox builder, the transport dispatcher registers
- **Test kind**: Integration
- **Description**: Module builder registration theory over brokers
- **Behavior**: **`Use*Dispatch`** on inbox module for InMemory, AMQP, Azure, SQS, Kafka
- **Expected outcome**: **`IInboxDispatcher`** registered
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Registration/`)

#### `OutboxTransportEnvelopeMapperTests.BuildHeaders_ShouldMapAllMetadataFields`

- **Use case**: When outbox transport maps an envelope to wire headers, all metadata fields appear on the message
- **Test kind**: Unit
- **Description**: Transport header mapper for outbox
- **Behavior**: **`BuildHeaders`** on outbox envelope
- **Expected outcome**: Contract, trace, tenant, and routing headers populated
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_enabled_should_throw_before_publish`

- **Use case**: When validate-payload-before-dispatch is enabled, deserialization failure throws before broker publish
- **Test kind**: Unit
- **Description**: **`ValidatePayloadBeforeDispatch`** gate
- **Behavior**: Dispatch with invalid payload bytes
- **Expected outcome**: Exception before transport publish
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Inbox in-process dispatch to events | No (planned) | N/A |: |: |
| Webhook/gRPC outbox adapters | No (planned) | N/A |: |: |
| Multi-dispatcher within one module | No (by design) | N/A |: |: |

### Out-of-Scope Use Cases

- Inbox in-process event replay via `IEventMediator`
- Webhook and gRPC publication adapters
- Load-balanced multi-dispatcher registration
