# Inbox Ingress

**ID:** `durable-core.inbox-ingress`
**Maturity:** GA (AMQP); Beta (Kafka, AWS SQS, Azure Service Bus); Extension (InMemory tests)

## Summary

Consume external broker deliveries and accept them into inbox storage with broker-scoped idempotency and configurable safety defaults.

## What It Does

Ingress extensions register a background consumer that maps `TransportMessage` to `InboxAcceptItem` and calls `IInbox.AcceptAsync`. On successful store accept, ingress acknowledges the broker delivery. Transient failures may requeue when `RequeueOnFailure` is enabled; poison messages discard per ingress policy. Ingress does not execute handlers; the inbox processor does that later.

## Public Surface

### Module Registration

| Extension | Package | Maturity |
| --- | --- | --- |
| `UseAmqpIngress(configure)` | `LiteBus.Inbox.Ingress.Amqp` | GA |
| `UseInMemoryIngress(configure?)` | `LiteBus.Inbox.Ingress.InMemory` | Extension (tests and local development) |
| `UseKafkaIngress(configure)` | `LiteBus.Inbox.Ingress.Kafka` | Beta |
| `UseAwsSqsIngress(configure)` | `LiteBus.Inbox.Ingress.AwsSqs` | Beta |
| `UseAzureServiceBusIngress(configure)` | `LiteBus.Inbox.Ingress.AzureServiceBus` | Beta |

Register the matching `Add*Transport(...)` once at the root, then register ingress inside **`AddInbox(...)`**. Each ingress extension adds its feature child and background service to the host manifest; it does not own broker connectivity.

### Core Types

- **`TransportInboxIngressConsumer`**: Background loop; maps deliveries, calls accept, applies ack policy
- **`TransportInboxIngressHandler`**: Maps **`TransportMessage`** to **`InboxAcceptItem`** and invokes **`IInbox`**
- **`TransportInboxIngressMapper`**: Header and identity mapping (idempotency, visibility, tenant, trace)
- **`IngressAckPolicy`**: Classifies exceptions for requeue vs discard

### Configuration (`TransportInboxIngressOptions`)

| Option | Default | Role |
| --- | --- | --- |
| `Destination` | (required) | Broker address the consumer subscribes to |
| `PrefetchCount` | Broker-specific | Max unacknowledged deliveries |
| `RequireStableIdentity` | `true` | Reject deliveries without broker message id |
| `TrustApplicationHeaders` | `false` | Honor app idempotency, identity, and tenant headers |
| `MaxMessageBytes` | 4 MiB | Reject oversized bodies before accept |
| `EnableBatchAccept` | `false` | Buffer deliveries for **`AcceptBatchAsync`** flush |
| `BatchMaxWait` | 200 ms | Partial batch flush delay when batch accept enabled |
| `RequeueOnFailure` | `true` (broker-specific) | Transient accept failure requeue |
| `AuthorizeDeliveryAsync` | `null` | Host callback before accept |

### Runtime Path

1. Consumer receives **`TransportMessage`**
2. Optional **`AuthorizeDeliveryAsync`** runs
3. Mapper builds **`InboxAcceptItem`** with broker-scoped identity and idempotency (see idempotency capability)
4. **`IInbox.AcceptAsync`** or **`AcceptBatchAsync`** writes to singleton store (not transactional writer)
5. On success, broker **`AcceptAsync`** acknowledges delivery
6. Ack failure after accept: log, increment **`ingress.ack_failed_after_accept`**, requeue for idempotent redelivery

### Extension Points

- **`AuthorizeDeliveryAsync`**: Application authorization before deserialization and accept
- Custom broker adapters implement ingress handler wiring in **`LiteBus.Inbox.Ingress.*`** packages
- Opt out: **`DisableIngressConsumer`** flag on module registration (see PostgreSQL module registration tests)

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Ingress` | Shared handler, consumer, mapper, telemetry |
| `LiteBus.Inbox.Ingress.*` | Broker modules |
| `LiteBus.Transport.*` | Explicit root `IMessageConsumer` implementations |

## Requires

- Inbox module with storage registered
- Matching root transport registered through `Add*Transport(...)`
- `IInbox` singleton store (auto-commit path)
- Contract registration for ingested message types

## Invariants

- Ingress uses `IInbox`, not transactional writers
- Default maps broker delivery id to supplied identity and `ingress:{destination}:{brokerMessageId}` idempotency key
- Ack failure after accept logs `ingress.ack_failed_after_accept`; redelivery absorbed by idempotency
- Visibility headers honored on ingress mapping
- Kafka: manual offset commit; pair with inbox idempotency

## Non-Goals

- Outbox ingress (events enter via application enqueue)
- HTTP webhook ingress (planned)
- Executing handlers at ingress boundary

## Observability

### Ingress Ack Failure Counter

| Instrument | Constant | Meter | Kind |
| --- | --- | --- | --- |
| `ingress.ack_failed_after_accept` | `LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName` | `LiteBus.Inbox` | Counter |

- **When incremented**: Broker **`AcceptAsync`** throws after **`IInbox`** accept succeeded
- **Recording site**: **`TransportInboxIngressTelemetry.RecordAckFailedAfterAccept()`** in **`TransportInboxIngressConsumer`**
- **Registration**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Log companion**: EventId 3004 (`AckFailedAfterAccept`); consumer requeues so idempotent accept absorbs redelivery

### Transport Process Tracing

- **Activity**: `process {destination}` on activity source `LiteBus.Transport`
- **When started**: Per delivery in `TransportConsumerHandlerInvoker`
- **Registration**: `AddLiteBusTransportInstrumentation()`

### Circuit Breaker (Shared Transport)

| Instrument | Tag | When relevant |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `litebus.transport.broker` | Root transport breaker blocks connection open |
| `litebus.transport.circuit_breaker.failure_count` | `litebus.transport.broker` | Rising failures before open |

Consumer handler failures do not increment the breaker; ack/requeue policy applies instead.

### Operational Logs (No Public Meter)

| Event | When emitted |
| --- | --- |
| Poison discard | Malformed JSON, unknown contract, oversize body |
| Requeue decision | Transient store or ack failure when **`RequeueOnFailure`** enabled |
| Missing stable identity | **`RequireStableIdentity`** violation before accept |

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (ingress section)
- [Inbox AMQP ingress](../../integrations/inbox-amqp-ingress.md)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md) (broker ack table)
- [Architecture](../../architecture/README.md) (ingress axis)
- [Ingress telemetry](../ingress/telemetry.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`

### Covered Use Cases

#### `TransportInboxIngressHandlerTests.AcceptAsync_ShouldWriteEnvelopeToInboxStore`

- **Use case**: When ingress maps a broker delivery, a row is written to the inbox store
- **Test kind**: Unit
- **Description**: Core accept path through ingress handler
- **Behavior**: **`AcceptAsync`** on mapped transport message
- **Expected outcome**: Envelope row in store
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressHandlerTests.AcceptAsync_WhenBrokerRedelivers_ShouldNotCreateDuplicateRows`

- **Use case**: When a broker redelivers the same delivery id, ingress idempotency prevents duplicate rows
- **Test kind**: Unit
- **Description**: Broker redelivery simulation
- **Behavior**: Two accepts with same broker message id
- **Expected outcome**: Single row in store
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressHandlerTests.AcceptAsync_WhenBodyExceedsMaxMessageBytes_ShouldThrow`

- **Use case**: When the delivery body exceeds **`MaxMessageBytes`**, ingress rejects before store write
- **Test kind**: Unit
- **Description**: Size guard on ingress handler
- **Behavior**: Accept with oversized body
- **Expected outcome**: Exception before store write
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressHandlerTests.AcceptAsync_WithAuthorizationCallback_ShouldAuthorizeBeforeStoreWrite`

- **Use case**: The host authorization callback inspects the original delivery before inbox acceptance
- **Test kind**: Unit
- **Description**: Records the callback message instance and cancellation token
- **Behavior**: Authorization succeeds, then deserialization and store acceptance continue
- **Expected outcome**: Callback runs once with the original arguments; one pending inbox row is written
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressConsumerTests.HandleDeliveryAsync_WhenAuthorizationRejects_ShouldDiscardWithoutAccept`

- **Use case**: A terminal authorization rejection never reaches inbox storage
- **Test kind**: Unit
- **Description**: The host callback throws `InboxIngressException` with requeue-on-failure enabled
- **Behavior**: Shared acknowledgement policy classifies the rejection as terminal
- **Expected outcome**: No inbox accept or broker acknowledgement; one negative acknowledgement without requeue
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressConsumerTests.HandleDeliveryAsync_WhenAckFailsAfterAccept_ShouldNotDiscard`

- **Use case**: Broker acknowledgement failure after a successful inbox write preserves idempotent redelivery
- **Test kind**: Unit
- **Description**: Inbox acceptance succeeds, then the acknowledgement delegate throws
- **Behavior**: Consumer records the public failure counter and returns the delivery to the queue
- **Expected outcome**: One inbox accept, one requeue, no discard, and `ingress.ack_failed_after_accept` increments
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `AmqpInboxIngressHandlerTests.AcceptAsync_ShouldDeserializeAndWriteToInboxWithMappedHeaders`

- **Use case**: When AMQP ingress receives a valid delivery, it deserializes and writes with mapped headers
- **Test kind**: Unit
- **Description**: AMQP adapter handler path
- **Behavior**: AMQP **`AcceptAsync`** with contract and metadata headers
- **Expected outcome**: Row in store with mapped metadata
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`)

#### `AmqpInboxIngressHandlerTests.AcceptAsync_WhenContractHeaderMissing_ShouldThrow`

- **Use case**: When the contract header is missing, AMQP ingress fails before store write
- **Test kind**: Unit
- **Description**: Contract validation at ingress edge
- **Behavior**: Accept without contract header
- **Expected outcome**: Exception; no store write
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`)

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldMapOptionalHeadersWhenTrusted`

- **Use case**: When application headers are trusted, optional tenant and idempotency headers map to accept metadata
- **Test kind**: Unit
- **Description**: **`TrustApplicationHeaders`** enabled
- **Behavior**: **`ToInboxAcceptMetadata`** with app headers
- **Expected outcome**: Tenant and idempotency from headers
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_WhenBrokerIdMissingAndRequired_ShouldThrow`

- **Use case**: When stable identity is required and broker id is missing, mapping fails before accept
- **Test kind**: Unit
- **Description**: **`RequireStableIdentity`** default behavior
- **Behavior**: Mapper on message without broker delivery id
- **Expected outcome**: Exception
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `IngressAckPolicyTests.ShouldRequeue_WhenRequeueOnFailureTrueAndDiscardException_ShouldReturnFalse`

- **Use case**: When requeue is enabled but the exception is classified as poison, ingress does not requeue
- **Test kind**: Unit
- **Description**: Ack policy poison classification (Theory)
- **Behavior**: **`ShouldRequeue`** with discard exception type
- **Expected outcome**: Discard (no requeue)
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `IngressAckPolicyTests.ShouldRequeue_WhenRequeueOnFailureTrueAndTransientIOException_ShouldReturnTrue`

- **Use case**: When requeue is enabled and the failure is transient, ingress requeues the delivery
- **Test kind**: Unit
- **Description**: Ack policy transient classification
- **Behavior**: **`ShouldRequeue`** with transient **`IOException`**
- **Expected outcome**: Requeue
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `AmqpInboxIngressEndToEndTests.PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: When a message is published through RabbitMQ, ingress accepts, processor runs, and command dispatches
- **Test kind**: Integration
- **Description**: Full AMQP ingress loop
- **Behavior**: Publish through broker into ingress module
- **Expected outcome**: Command executed end-to-end
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `AmqpInboxIngressFailureTests.UnknownContract_ShouldNackWithoutRequeueAndSkipStore`

- **Use case**: When an unknown contract arrives on AMQP ingress, the message is nacked without requeue and no row is stored
- **Test kind**: Integration
- **Description**: Poison message handling for unknown contract
- **Behavior**: Publish with unregistered contract name
- **Expected outcome**: No store row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `AmqpInboxIngressFailureTests.InvalidJson_ShouldNackWithoutRequeueAndSkipStore`

- **Use case**: When malformed JSON arrives on AMQP ingress, the message is nacked without requeue
- **Test kind**: Integration
- **Description**: Poison message handling for invalid payload
- **Behavior**: Publish invalid JSON body
- **Expected outcome**: No store row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `AmqpInboxIngressBatchIntegrationTests.EnableBatchAccept_AtPrefetchThreshold_ShouldFlushAllMessages`

- **Use case**: When batch accept is enabled and prefetch threshold is reached, all buffered messages flush to the store
- **Test kind**: Integration
- **Description**: Batch ingress at prefetch threshold
- **Behavior**: **`EnableBatchAccept`** with multiple deliveries
- **Expected outcome**: All messages accepted in batch flush
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `InMemoryInboxIngressModuleIntegrationTests.UseInMemoryIngress_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: When in-memory ingress is configured, accept, process, and dispatch complete end-to-end
- **Test kind**: Integration
- **Description**: In-memory broker ingress module
- **Behavior**: Full module wiring with in-memory transport
- **Expected outcome**: Command executed
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `KafkaInboxIngressEndToEndIntegrationTests.PublishThroughKafka_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: When a message is published through Kafka, ingress accepts and the command executes (beta path)
- **Test kind**: Integration
- **Description**: Kafka ingress end-to-end
- **Behavior**: Publish through Kafka into ingress module
- **Expected outcome**: Command executed
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`)

#### `AwsSqsInboxIngressEndToEndIntegrationTests.PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: When a message is published through SQS, ingress accepts and the command executes (beta path)
- **Test kind**: Integration
- **Description**: AWS SQS ingress end-to-end
- **Behavior**: Publish through SQS into ingress module
- **Expected outcome**: Command executed
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`)

#### `AzureServiceBusInboxIngressEndToEndIntegrationTests.PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: When a message is published through Azure Service Bus, ingress accepts and the command executes (beta path)
- **Test kind**: Integration
- **Description**: Azure Service Bus ingress end-to-end
- **Behavior**: Publish through Service Bus into ingress module
- **Expected outcome**: Command executed
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`)

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept`

- **Use case**: When requeue is enabled and the store fails transiently, the delivery is eventually accepted
- **Test kind**: Integration
- **Description**: **`RequeueOnFailure`** with simulated transient store failure
- **Behavior**: Ingress consumer retry loop
- **Expected outcome**: Row eventually accepted
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite`

- **Use case**: When requeue is disabled and the message is poison, ingress discards without store write
- **Test kind**: Integration
- **Description**: Requeue disabled poison path
- **Behavior**: Invalid delivery with **`RequeueOnFailure`** false
- **Expected outcome**: No store row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `InMemoryIngressHeaderEdgeCaseIntegrationTests.MissingContractName_ShouldDiscardWithoutStoreWrite`

- **Use case**: When contract name header is missing, ingress discards without store write
- **Test kind**: Integration
- **Description**: Header edge case on in-memory ingress
- **Behavior**: Delivery without contract header
- **Expected outcome**: Discarded; no row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `KafkaInboxIngressFailureIntegrationTests.UnknownContract_ShouldNotWriteToStore`

- **Use case**: When Kafka ingress receives an unknown contract, no row is written to the store
- **Test kind**: Integration
- **Description**: Kafka failure path for unknown contract
- **Behavior**: Publish with unregistered contract
- **Expected outcome**: No store row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`)

#### `AmqpInboxIngressModuleRegistrationTests.UseAmqpIngress_WithTransportModule_ShouldRegisterIngressHandler`

- **Use case**: When AMQP ingress is registered with transport module, the ingress handler is wired in DI
- **Test kind**: Unit
- **Description**: Module registration wiring
- **Behavior**: **`UseAmqpIngress`** on inbox builder
- **Expected outcome**: Ingress handler registered
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`)

#### `PostgreSqlModuleRegistrationTests.DisableIngressConsumer_ShouldNotRegisterIngressHostedService`

- **Use case**: When ingress consumer is disabled, no ingress background service appears in the host manifest
- **Test kind**: Integration
- **Description**: Opt-out flag on module registration
- **Behavior**: Build module with ingress consumer disabled
- **Expected outcome**: No ingress hosted service in manifest
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

### Out-of-Scope Use Cases

- Outbox ingress (events enter via application enqueue)
- Handler execution at ingress boundary
- HTTP webhook ingress adapter
