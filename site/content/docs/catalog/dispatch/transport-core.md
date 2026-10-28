# Shared Transport Dispatchers

- **ID**: `dispatch.transport-core`
- **Name**: Shared transport dispatchers
- **Maturity**: GA
- **Summary**: Broker-neutral `TransportInboxDispatcher` and `TransportOutboxDispatcher` publish leased envelopes through `ITransportPublisher` with shared options and optional tenant routing.

## What It Does

`LiteBus.Inbox.Dispatch` and `LiteBus.Outbox.Dispatch` host the shared transport dispatch implementation. Each dispatcher resolves the contract from the envelope, unprotects the payload when a protector is registered, and validates deserialization when validation is enabled. It then resolves a transport route, builds publish headers, and calls `ITransportPublisher.PublishAsync`.

`TransportInboxDispatchModule` and `TransportOutboxDispatchModule` register the dispatcher and require a matching root transport module. Broker-specific `Use*Dispatch` extensions add only the feature bridge wiring. Transport outbox modules expose `DefaultHookFailurePolicy = CompleteDespiteHookFailure`.

Route resolution order:

| Axis | Priority |
| --- | --- |
| Inbox | `ITenantRoutingStrategy` if registered, else `ResolveRoute` delegate, else `ContractName` |
| Outbox | `ITenantRoutingStrategy` if registered, else `ResolveRoute` delegate, else non-empty `Topic`, else `ContractName` |

The tenant strategy takes precedence whenever one is registered. On the outbox axis, a `ResolveRoute` delegate or tenant routing strategy that returns null or whitespace fails the dispatch with a `LiteBusDispatchException` that names the envelope. On the inbox axis, a `ResolveRoute` delegate that returns null or whitespace fails the dispatch with an `InboxDispatchException` that names the envelope. On the outbox axis, the strategy is set through `TransportOutboxDispatcherOptions.TenantRoutingStrategy`. On the inbox axis, it is constructor-injected into `TransportInboxDispatcher`.

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

- Stored payload bytes are published as-is after an optional unprotect. Deserialization only validates the payload when `ValidatePayloadBeforeDispatch` is true.
- The `MessageId` on the transport request is the envelope GUID string.
- The outbox dispatcher fills `MessageGroupId` and `DeduplicationKey` on every publish request. Broker adapters apply them only where the broker supports ordered groups (SQS FIFO queue URLs ending in `.fifo`).
- Dispatch and ingress share one explicitly selected root transport module.
- Dispatch throws on transport failure. The processor owns retry and dead-letter state.

## Non-Goals

- It does not consume from brokers. The ingress axis owns intake.
- It does not deserialize and invoke mediators. In-process dispatch owns that path.
- It does not persist terminal store outcomes. The processor owns persistence after dispatch returns.

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
| Flow | Resolves the contract, unprotects the payload, optionally validates deserialization, resolves the route, starts the publish activity, then calls `ITransportPublisher.PublishAsync` |

It throws when contract resolution, validation, or transport publish fails. It does not catch transport exceptions.

### `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch` |
| Implements | `IOutboxDispatcher.DispatchAsync` |
| Flow | Same as the inbox transport dispatcher, except the route prefers the tenant strategy, then the resolver, then the envelope `Topic`, then the contract name |
| FIFO addressing | `MessageGroupId` is the tenant id when present, otherwise the contract name. `DeduplicationKey` is the idempotency key when present, otherwise the message id. The inbox dispatcher sets neither |

### `TransportInboxDispatchModule.Build(IModuleConfiguration)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch` |
| Registers | The `TransportInboxDispatcherOptions` singleton, and `TransportInboxDispatcher` as the `IInboxDispatcher` |
| Dependencies | Requires the matching root transport module, and duplicate dispatcher registration fails during composition |

### `TransportOutboxDispatchModule.Build(IModuleConfiguration)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch` |
| Registers | The `TransportOutboxDispatcherOptions` singleton, and `TransportOutboxDispatcher` as the `IOutboxDispatcher` |
| `DefaultHookFailurePolicy` | `CompleteDespiteHookFailure` (transport outbox default) |

### `TransportInboxDispatcherOptions` / `TransportOutboxDispatcherOptions`

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange, topic, queue URL, or InMemory destination name |
| `ContentType` | `application/json` | Wire MIME type |
| `Persistent` | `true` | Broker durability flag (AMQP) |
| `Mandatory` | `true` | Fails the publish with `AmqpMessageReturnedException` when the message is unroutable (AMQP) |
| `ResolveRoute` | `null` | Per-envelope route override delegate that wins over `Topic` on the outbox axis and must return a non-empty route |
| `TenantRoutingStrategy` | `null` | Outbox only. A per-tenant destination override, registered as the `ITenantRoutingStrategy` that the dispatcher resolves |
| `ValidatePayloadBeforeDispatch` | `false` | Deserializes before publish to catch contract wiring errors |

### Optional Dependencies (Constructor Injection)

| Type | Role |
| --- | --- |
| `IInboxPayloadProtector` / `IOutboxPayloadProtector` | Unprotects the stored payload before publish |
| `ITenantRoutingStrategy` | Per-tenant destination and route override |

## Observability

| Signal | Constant / name | Tags / attributes | When |
| --- | --- | --- | --- |
| Send activity | `send {destination}` | Activity source `LiteBus.Transport`, with the required messaging operation tags, the destination, the message id, the conversation id, and broker-specific route tags | Started by the concrete transport publisher around its SDK send call |
| Processor dispatch duration | `litebus.inbox.processor.dispatch_duration` / `litebus.outbox.processor.dispatch_duration` | A histogram in milliseconds | The processor envelope handler wraps dispatch, covering both transport and in-process paths |
| Circuit breaker open | `litebus.transport.circuit_breaker.open` | Tag `litebus.transport.broker` (`amqp`, `kafka`, `sqs`, `azure_service_bus`, `inmemory`) | The broker adapter records consecutive publish or connection failures |
| Circuit breaker failure count | `litebus.transport.circuit_breaker.failure_count` | Same broker tag | The adapter increments it on counted publish failures |
| Processor pass counters | `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered`, `litebus.outbox.processor.published` / `failed` / `dead_lettered` | None | Incremented after the dispatch outcome is persisted |

The dispatcher copies trace context from envelope headers onto `TransportPublishRequest.Headers` for downstream `process {destination}` correlation. No dispatch-specific meter exists beyond the processor and transport layers.

Register transport tracing through `AddLiteBusTransportInstrumentation()` from `LiteBus.Transport.Extensions.OpenTelemetry`. Processor metrics require `AddLiteBusInboxMetrics()` or `AddLiteBusOutboxMetrics()`.

## References

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
| `DispatchAsync_WhenIdempotencyKeyAndTenantIdAreSet_ShouldMapFifoFields` | `LiteBus.Outbox.UnitTests` (`Dispatch/`, `TransportOutboxDispatcherFifoFieldsTests`) |
| `DispatchAsync_WhenIdempotencyKeyAndTenantIdAreMissing_ShouldFallBackToMessageIdAndContractName` | `LiteBus.Outbox.UnitTests` (`Dispatch/`, `TransportOutboxDispatcherFifoFieldsTests`) |
| `DispatchAsync_WhenIdempotencyKeyAndTenantIdAreBlank_ShouldFallBackToMessageIdAndContractName` | `LiteBus.Outbox.UnitTests` (`Dispatch/`, `TransportOutboxDispatcherFifoFieldsTests`) |
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

- The inbox `ValidatePayloadBeforeDispatch = true` branch.
- The `ITenantRoutingStrategy` route selection on the inbox and outbox paths.
- Unprotecting the payload before publish, for both dispatchers.
- Unreachable-broker coverage for the AMQP and Azure outbox, to match the other brokers.

### Out-of-Scope

- Broker consumption, which the ingress axis owns
- Deserializing and invoking in-process mediators, which the in-process dispatch axis owns
- Persisting terminal store outcomes after dispatch returns, which the processor and storage axes own
