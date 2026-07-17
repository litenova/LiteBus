# Inbox Azure Service Bus Dispatch

## Header

- **ID**: `dispatch.inbox.azure-service-bus`
- **Name**: Inbox Azure Service Bus dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Azure Service Bus queues or topics via `UseAzureServiceBusDispatch`.

## What It Does

The extension registers `TransportInboxDispatchModule` with `AzureServiceBusTransportModule`. `DefaultDestination` maps to queue or topic name; route maps to subject. Behavior follows the shared transport inbox dispatcher with Azure-specific ack semantics on the consumer side (ingress or remote workers).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | Registration glue |
| `LiteBus.Inbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.AzureServiceBus` | Azure SDK adapter |

## Requires

- `dispatch.transport-core`
- `transport.azure-service-bus`
- `durable-core.inbox`

## Invariants

- One Azure Service Bus transport module per process.
- Publish path uses Service Bus message properties for LiteBus headers.
- Consumer reconnect uses exponential backoff in `AzureServiceBusConsumer` (transport layer).

## Non-Goals

- Does not manage Service Bus entity creation (application or infrastructure responsibility).
- Does not consume messages (ingress axis for intake).

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddInboxModule(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseAzureServiceBusDispatch(
            options =>
            {
                options.DefaultDestination = "commands";
                options.ResolveRoute = envelope => envelope.ContractName;
            },
            new AzureServiceBusTransportOptions
            {
                ConnectionString = "<namespace-connection-string>",
                ClientId = "orders-dispatcher"
            });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAzureServiceBusDispatch(Action<TransportInboxDispatcherOptions>, AzureServiceBusTransportOptions)` | Registers inbox transport dispatcher with Azure Service Bus transport module |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Shared publish flow for inbox leases |

`AzureServiceBusTransportOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `ConnectionString` | required | Service Bus namespace connection |
| `ClientId` | `null` | SDK client identifier |
| `ConsumerErrorRetryInterval` | `00:00:05` | Base delay for consumer restart |
| `ConsumerErrorRetryMaxInterval` | `00:01:00` | Max restart delay for repeated errors |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Queue or topic name, subject route, message id, and `messaging.system=servicebus` |
| `litebus.transport.circuit_breaker.*` | Broker tag `azure_service_bus` |
| `litebus.inbox.processor.dispatch_duration` | Full dispatch including Service Bus send |
| `process {destination}` | Ingress path only, not dispatch |

Optional live-namespace tests in `AzureServiceBusOptionalIntegrationTests` when env vars are configured.

## Deep Docs

- [Architecture.md: Transport resilience matrix](../../architecture/README.md#transport-resilience-capability-matrix)
- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AzureServiceBus/`) (emulator) |
| `ProcessPendingAsync_WithLiveConnection_ShouldPublishToAzureServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) (optional live namespace) |

### Untested

- Dispatch failure path with unreachable namespace in dispatch-specific suite.
- Circuit-breaker-open behavior for inbox Azure dispatch.
- Tenant routing strategy override coverage.

### Out-of-Scope

- Service Bus entity creation and IAM (application or infrastructure)
- Consuming messages (`ingress.inbox.azure-service-bus`)
