# Outbox Azure Service Bus Dispatch

## Header

- **ID**: `dispatch.outbox.azure-service-bus`
- **Name**: Outbox Azure Service Bus dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Azure Service Bus with contract headers via `UseAzureServiceBusDispatch`.

## What It Does

Registers shared `TransportOutboxDispatcher` with Azure Service Bus transport. Outbox processor publication targets a queue or topic configured in `TransportOutboxDispatcherOptions.DefaultDestination`. Subject/route follows outbox topic metadata or contract name fallback.

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

- Default hook failure policy: `CompleteDespiteHookFailure`.
- At-least-once publication semantics match other transport outbox adapters.
- Abandon/requeue behavior applies on consumer side when downstream uses Service Bus ingress.

## Non-Goals

- Does not implement Service Bus sessions or duplicate detection configuration.
- Does not batch multiple outbox rows into one Service Bus message.

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
| `OutboxModuleBuilder.UseAzureServiceBusDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers outbox transport dispatcher that requires the root Azure Service Bus transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | Defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes outbox envelope with canonical headers |

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
| `send {destination}` | Service Bus send span with destination and subject route |
| `litebus.transport.circuit_breaker.*` | Tag `azure_service_bus` |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | Processor terminal counters |
| `litebus.outbox.processor.dispatch_duration` | Histogram including Service Bus SDK send time |

## Deep Docs

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

- `CompleteDespiteHookFailure` path with after-dispatch hook failure.
- Unreachable namespace and breaker-open dispatch behavior.
- Session and duplicate detection interactions.

### Out-of-Scope

- Service Bus session semantics and broker duplicate detection
- Batching multiple outbox rows into one Service Bus message
