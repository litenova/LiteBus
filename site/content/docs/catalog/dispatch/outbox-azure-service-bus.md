# Outbox Azure Service Bus Dispatch

## Header

- **ID**: `dispatch.outbox.azure-service-bus`
- **Name**: Outbox Azure Service Bus dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Azure Service Bus with contract headers via `UseAzureServiceBusDispatch`.

## What It Does

This extension registers the shared `TransportOutboxDispatcher` with the Azure Service Bus transport. Outbox processor publication targets the queue or topic configured in `TransportOutboxDispatcherOptions.DefaultDestination`. The subject or route follows the outbox topic metadata, falling back to the contract name.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | Registration glue |
| `LiteBus.Outbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.AzureServiceBus` | Azure SDK adapter |

## Requires

- `dispatch.transport-core`
- `transport.azure-service-bus`
- `durable-core.outbox`

## Invariants

- The default hook failure policy is `CompleteDespiteHookFailure`.
- At-least-once publication semantics match other transport outbox adapters.
- Abandon and requeue behavior applies on the consumer side when downstream uses Service Bus ingress.

## Non-Goals

- It does not implement Service Bus sessions or duplicate detection configuration.
- It does not batch multiple outbox rows into one Service Bus message.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAzureServiceBusTransport(new AzureServiceBusTransportOptions
    {
        ConnectionString = "<namespace-connection-string>",
        ClientId = "billing-outbox"
    });

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseAzureServiceBusDispatch(
            options =>
            {
                options.DefaultDestination = "events";
                options.ResolveRoute = envelope => envelope.Topic ?? envelope.ContractName;
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseAzureServiceBusDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers the outbox transport dispatcher, which requires the root Azure Service Bus transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | The policy defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes the outbox envelope with canonical headers |

`AzureServiceBusTransportOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `ConnectionString` | required | Service Bus namespace connection |
| `ClientId` | `null` | SDK client identifier |
| `ConsumerErrorRetryInterval` | `00:00:05` | Base restart delay |
| `ConsumerErrorRetryMaxInterval` | `00:01:00` | Max restart delay |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Service Bus send span with the destination and subject route |
| `litebus.transport.circuit_breaker.*` | Tag `azure_service_bus` |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | The processor terminal counters |
| `litebus.outbox.processor.dispatch_duration` | A histogram including the Service Bus SDK send time |

## References

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToServiceBusQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`) (emulator) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AzureServiceBus/`) |

### Untested

- The `CompleteDespiteHookFailure` path with after-dispatch hook failure.
- The unreachable namespace and breaker-open dispatch behavior.
- Session and duplicate detection interactions.

### Out-of-Scope

- Service Bus session semantics and broker duplicate detection
- Batching multiple outbox rows into one Service Bus message
