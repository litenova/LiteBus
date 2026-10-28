# Inbox Azure Service Bus Dispatch

## Header

- **ID**: `dispatch.inbox.azure-service-bus`
- **Name**: Inbox Azure Service Bus dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Azure Service Bus queues or topics via `UseAzureServiceBusDispatch`.

## What It Does

The extension registers `TransportInboxDispatchModule` with `AzureServiceBusTransportModule`. `DefaultDestination` holds the queue or topic name, and the route becomes the subject. Dispatch behaves as the shared transport inbox dispatcher describes. Azure-specific acknowledgment semantics apply on the consumer side, in ingress or in remote workers.

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

- A composition holds no more than one Azure Service Bus transport module.
- The publish path carries LiteBus headers in Service Bus message properties.
- The consumer reconnects with exponential backoff in `AzureServiceBusConsumer`. The transport layer owns that behavior.

## Non-Goals

- It does not manage Service Bus entity creation, because that is an application or infrastructure responsibility.
- It does not consume messages, because the ingress axis owns intake.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAzureServiceBusTransport(new AzureServiceBusTransportOptions
    {
        ConnectionString = "<namespace-connection-string>",
        ClientId = "orders-dispatcher"
    });

    litebus.AddInbox(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseAzureServiceBusDispatch(
            options =>
            {
                options.DefaultDestination = "commands";
                options.ResolveRoute = envelope => envelope.ContractName;
            });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAzureServiceBusDispatch(Action<TransportInboxDispatcherOptions>)` | Registers the inbox transport dispatcher, which requires the root Azure Service Bus transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | The shared publish flow for inbox leases |

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
| `send {destination}` | The queue or topic name, the subject route, the message id, and `messaging.system=servicebus` |
| `litebus.transport.circuit_breaker.*` | Broker tag `azure_service_bus` |
| `litebus.inbox.processor.dispatch_duration` | The full dispatch, including the Service Bus send |
| `process {destination}` | It appears on the ingress path only, never on dispatch |

The optional live-namespace tests in `AzureServiceBusOptionalIntegrationTests` run when the environment variables are configured.

## References

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

- The dispatch failure path with an unreachable namespace in the dispatch-specific suite.
- The circuit-breaker-open behavior for inbox Azure dispatch.
- Tenant routing strategy override coverage.

### Out-of-Scope

- Service Bus entity creation and IAM (an application or infrastructure responsibility)
- Consuming messages (see `ingress.inbox.azure-service-bus`)
