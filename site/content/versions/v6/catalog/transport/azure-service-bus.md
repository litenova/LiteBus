# Azure Service Bus Transport

- **ID**: `transport.azure-service-bus`
- **Name**: Azure Service Bus transport
- **Maturity**: Beta
- **Summary**: Azure Service Bus adapter with explicit message settlement and canonical property mapping.

## What It Does

`AzureServiceBusTransportModule` registers `AzureServiceBusPublisher` and `AzureServiceBusConsumer` using `Azure.Messaging.ServiceBus`. Publish maps `Destination` to queue or topic and maps `Route` to `Subject`. Consume uses peek-lock with `AutoCompleteMessages = false` and explicit settlement delegates.

Beta tier note: adapter behavior is validated in durable integration tests, with optional live-connection coverage kept separate from emulator coverage.

## Public Surface

### Registration and Runtime Types

| API | Role |
| --- | --- |
| `AzureServiceBusTransportModule` | Registers Service Bus transport services |
| `AzureServiceBusPublisher.PublishAsync` | Sends broker message |
| `AzureServiceBusConsumer.StartAsync` | Runs processor consume loop |
| `AzureServiceBusMessageMapper` | Maps transport request and application properties |
| `AzureServiceBusConnectivityDiagnosticCheck` | Peeks a configured entity for host readiness |
| `AzureServiceBusDiagnosticTarget` | Queue or topic-subscription readiness target |
| `LiteBusTransportAzureServiceBusTelemetry.MeterName` | Reserved Service Bus meter name |

### Options

| Property | Default | Purpose |
| --- | --- | --- |
| `ConnectionString` | required | Service Bus connection |
| `ClientId` | `null` | Service Bus client identifier |
| `ConnectivityCheckTarget` | `null` | Queue or subscription peeked by readiness; missing reports degraded |
| `ConsumerErrorRetryInterval` | `5s` | Base restart delay after processor errors |
| `ConsumerErrorRetryMaxInterval` | `1m` | Max restart delay |

### Ack Mapping

| Transport call | Service Bus action |
| --- | --- |
| `AcceptAsync` | `CompleteMessageAsync` |
| `DiscardAsync` | `DeadLetterMessageAsync` |
| `ReturnToQueueAsync` | `AbandonMessageAsync` |

## Packages

- `LiteBus.Transport.AzureServiceBus`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`
- Service Bus namespace or emulator connection string

## Invariants

- Consumer uses manual settlement (`AutoCompleteMessages = false`).
- Redelivery hint uses `DeliveryCount > 1`.
- Circuit-breaker metrics register with broker tag `azure_service_bus`.

## Non-Goals

- Event Hubs support.
- Entity topology and subscription provisioning.
- Cross-region failover routing.

## Observability

### Metrics

| Item | Value |
| --- | --- |
| Shared meter | `LiteBus.Transport` |
| Broker tag | `litebus.transport.broker="azure_service_bus"` |
| Shared gauges | `litebus.transport.circuit_breaker.open`, `litebus.transport.circuit_breaker.failure_count` |
| Reserved adapter meter | `LiteBus.Transport.AzureServiceBus` |
| OpenTelemetry registration | `AddLiteBusTransportMetrics()` |

### Tracing

- Activity source `LiteBus.Transport`
- Spans `send {destination}` and `process {destination}` with `messaging.system=servicebus`

### Diagnostics

- Diagnostic check id: `transport.azure_service_bus.connectivity`
- `AzureServiceBusQueueDiagnosticTarget` peeks a queue.
- `AzureServiceBusSubscriptionDiagnosticTarget` peeks a topic subscription.
- The peek does not lock or settle messages. Grant entity listen permission. Missing target configuration reports degraded.
- See the current [`PeekMessagesAsync` contract](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusreceiver.peekmessagesasync?view=azure-dotnet).

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_ShouldRejectDuplicateTransportRegistration` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `Constructor_ShouldRejectEmptyConnectionString` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `CheckAsync_WithoutTarget_ShouldReturnDegraded` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand` readiness assertion | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`) |
| `ProcessPendingAsync_WithLiveConnection_ShouldPublishToAzureServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |

### Untested

- Raw Service Bus publisher and consumer tests outside durable wrappers.
- Circuit-breaker-open dispatch scenarios for Service Bus path.
- Gauge export assertions for broker tag `azure_service_bus`.

### Out-of-Scope

- Event Hubs and non-Service Bus SDKs.
- Automatic topology management.

## Deep Docs

- [Azure-Service-Bus-Transport.md](../../integrations/azure-service-bus.md)
- [Architecture.md](../../architecture/README.md)
- [manual-acknowledgement.md](manual-acknowledgement.md)
