# Shared Transport Dispatchers

- **ID**: `dispatch.transport-core`
- **Name**: Shared transport dispatchers
- **Maturity**: GA
- **Summary**: Broker-neutral `TransportInboxDispatcher` and `TransportOutboxDispatcher` publish leased envelopes through `ITransportPublisher` with shared options and optional tenant routing.

## What It Does

`LiteBus.Inbox.Dispatch` and `LiteBus.Outbox.Dispatch` host the shared transport dispatch implementation. Each dispatcher resolves the contract from the envelope, optionally decrypts the payload, optionally validates deserialization, resolves a transport route, builds publish headers, and calls `ITransportPublisher.PublishAsync`.

`TransportInboxDispatchModule` and `TransportOutboxDispatchModule` register the dispatcher and require a matching root transport module. Broker-specific `Use*Dispatch` extensions contribute only feature bridge wiring. Transport outbox modules expose `DefaultHookFailurePolicy = CompleteDespiteHookFailure`.

Route resolution order:

| Axis | Priority |
| --- | --- |
| Inbox | `ITenantRoutingStrategy` if registered, else `ResolveRoute` delegate, else `ContractName` |
| Outbox | `ITenantRoutingStrategy` if registered, else non-empty `Topic`, else `ResolveRoute` delegate, else `ContractName` |

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch` | Inbox transport dispatcher and module |
| `LiteBus.Outbox.Dispatch` | Outbox transport dispatcher and module |
| `LiteBus.Transport.Abstractions` | `ITransportPublisher`, `TransportPublishRequest` |
| `LiteBus.Transport` | Tracing, shared header helpers |
| `LiteBus.Messaging` | Payload protection, serializer integration |

## Requires

- `runtime.contract-registry` (contract name/version to CLR type)
- `runtime.message-serialization`
- Matching root `transport.*` broker module registered through `Add*Transport(...)`
- `dispatch.registration`

## Invariants

- Stored payload bytes are published as-is after optional unprotect; deserialization is for validation only when `ValidatePayloadBeforeDispatch` is true.
- `MessageId` on the transport request is the envelope GUID string.
- One explicitly selected root transport module is shared by dispatch and ingress.
- Dispatch throws on transport failure; the processor owns retry and dead-letter state.

## Non-Goals

- Does not consume from brokers (ingress axis owns intake).
- Does not deserialize and invoke mediators (in-process dispatch owns that path).
- Does not persist terminal store outcomes (processor owns persistence after dispatch returns).

## Public Surface

```csharp
bus.AddKafkaTransport(new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
bus.AddOutbox(outbox => outbox.UseKafkaDispatch(
    options =>
    {
        options.DefaultDestination = "orders.events";
        options.ValidatePayloadBeforeDispatch = true;
        options.ResolveRoute = envelope => envelope.Topic ?? envelope.ContractName;
    }));
```

### `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch` |
| Implements | `IInboxDispatcher.DispatchAsync` |
| Flow | Resolve contract to unprotect payload to optional deserialize validation to resolve route to start publish activity to `ITransportPublisher.PublishAsync` |

Throws when contract resolution, validation, or transport publish fails. Does not catch transport exceptions.

### `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch` |
| Implements | `IOutboxDispatcher.DispatchAsync` |
| Flow | Same as inbox transport dispatcher; route prefers envelope `Topic` before resolver and contract name |

### `TransportInboxDispatchModule.Build(IModuleConfiguration)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch` |
| Registers | `TransportInboxDispatcherOptions` singleton, `IInboxDispatcher` to `TransportInboxDispatcher` |
| Dependencies | Requires the matching root transport module; duplicate dispatcher registration fails during composition |

### `TransportOutboxDispatchModule.Build(IModuleConfiguration)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch` |
| Registers | `TransportOutboxDispatcherOptions` singleton, `IOutboxDispatcher` to `TransportOutboxDispatcher` |
| `DefaultHookFailurePolicy` | `CompleteDespiteHookFailure` (transport outbox default) |

### `TransportInboxDispatcherOptions` / `TransportOutboxDispatcherOptions`

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange, topic, queue URL, or InMemory destination name |
| `ContentType` | `application/json` | Wire MIME type |
| `Persistent` | `true` | Broker durability flag (AMQP) |
| `Mandatory` | `false` | Fail publish when unroutable (AMQP) |
| `ResolveRoute` | `null` | Per-envelope route override delegate |
| `ValidatePayloadBeforeDispatch` | `false` | Deserialize before publish to catch contract wiring errors |

### Optional Dependencies (Constructor Injection)

| Type | Role |
| --- | --- |
| `IInboxPayloadProtector` / `IOutboxPayloadProtector` | Decrypt stored payload before publish |
| `ITenantRoutingStrategy` | Per-tenant destination/route override |

## Observability

| Signal | Constant / name | Tags / attributes | When |
| --- | --- | --- | --- |
| Send activity | `send {destination}` | Activity source `LiteBus.Transport`; required messaging operation tags plus destination, message id, conversation id, and broker-specific route tags | Started by the concrete transport publisher around its SDK send call |
| Processor dispatch duration | `litebus.inbox.processor.dispatch_duration` / `litebus.outbox.processor.dispatch_duration` | Histogram in ms | Processor envelope handler wraps dispatch (includes transport and in-process paths) |
| Circuit breaker open | `litebus.transport.circuit_breaker.open` | Tag `litebus.transport.broker` (`amqp`, `kafka`, `sqs`, `azure_service_bus`, `inmemory`) | Broker adapter records consecutive publish/connection failures |
| Circuit breaker failure count | `litebus.transport.circuit_breaker.failure_count` | Same broker tag | Incremented on counted publish failures |
| Processor pass counters | `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered`; `litebus.outbox.processor.published` / `failed` / `dead_lettered` | None on dispatch axis | Incremented after dispatch outcome is persisted |

Trace context from envelope headers is copied onto `TransportPublishRequest.Headers` for downstream `process {destination}` correlation. No dispatch-specific meter exists beyond processor and transport layers.

Register transport tracing through `AddLiteBusTransportInstrumentation()` from `LiteBus.Transport.Extensions.OpenTelemetry`. Processor metrics require `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`.

## Deep Docs

- [Architecture.md: Transport platform](../../architecture/README.md#transport-platform)
- [Architecture.md: Dispatch tracing](../../architecture/README.md#dispatch-tracing)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `DispatchAsync_ShouldPublishEnvelopeThroughTransport` | `LiteBus.Inbox.UnitTests` (`Dispatch/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`) |
| `ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopeCompleted` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`) |
| `DispatchAsync_ShouldPublishEnvelopeThroughTransport` | `LiteBus.Outbox.UnitTests` (`Dispatch/`) |
| `DispatchAsync_when_validate_payload_disabled_should_publish_without_deserializing` | `LiteBus.Outbox.UnitTests` (`Dispatch/`) |
| `DispatchAsync_when_validate_payload_enabled_should_throw_before_publish` | `LiteBus.Outbox.UnitTests` (`Dispatch/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`, `Dispatch/Outbox/InMemory/`, `Dispatch/Outbox/AzureServiceBus/`) |
| `ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToInMemoryDestination` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `UseAmqpDispatch_WithAmqpTransportModule_ShouldRegisterTransportOutboxDispatcher` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |

### Untested

- Inbox `ValidatePayloadBeforeDispatch = true` branch.
- `ITenantRoutingStrategy` route selection on inbox and outbox paths.
- Payload decryption branch for both dispatchers.
- AMQP and Azure outbox unreachable-broker coverage parity.

### Out-of-Scope

- Broker consumption (ingress axis)
- Deserializing and invoking in-process mediators (in-process dispatch axis)
- Persisting terminal store outcomes after dispatch returns (processor and storage axes)
