# Azure Service Bus Transport

**Production tier: Beta**

`LiteBus.Transport.AzureServiceBus` provides Azure Service Bus publish and peek-lock consume. LiteBus validates adapters with the official Service Bus emulator in CI; live namespace soak is optional.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.AzureServiceBus` | `ITransportPublisher`, `AzureServiceBusConsumer`, connection options |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | Inbox processor publish to Service Bus |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | Outbox processor publish to Service Bus |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | Service Bus intake into `IInbox.AcceptAsync` |

## Registration

```csharp
var transportOptions = new AzureServiceBusTransportOptions
{
    ConnectionString = configuration["ServiceBus:ConnectionString"]!,
    ConnectivityCheckTarget = new AzureServiceBusQueueDiagnosticTarget("orders-ingress")
};

builder.AddAzureServiceBusTransport(transportOptions);
builder.AddMessaging(_ => { });
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UseInMemoryStorage();
    inbox.UseAzureServiceBusDispatch(_ => { });
    inbox.UseAzureServiceBusIngress(ingress =>
    {
        ingress.UseOptions(new AzureServiceBusInboxIngressOptions
        {
            Destination = "orders-ingress",
            PrefetchCount = 10,
            MaxConcurrentCalls = 4,
            RequeueOnFailure = true
        });
    });
});
```

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `AzureServiceBusTransportOptions` | `ConnectionString` | required | Namespace or emulator connection |
| | `ConnectivityCheckTarget` | `null` | Queue or topic subscription peeked for readiness; missing reports degraded |
| | `ConsumerErrorRetryInterval` | 5 s | Processor restart backoff initial |
| | `ConsumerErrorRetryMaxInterval` | 60 s | Processor restart backoff cap |
| `AzureServiceBusInboxIngressOptions` | `RequeueOnFailure` | `true` | Abandon vs complete on failure |
| | `PrefetchCount` | 0 | Messages eagerly cached by the Azure client |
| | `MaxConcurrentCalls` | SDK default 1 | Azure processor callback concurrency |
| | `Safety.MaxInFlightMessages` | 32 | Final LiteBus handler admission cap |

Azure defines `PrefetchCount` and `MaxConcurrentCalls` as separate processor settings. Keep the prefetch cache large enough to feed the requested callback concurrency, then use `Safety.MaxInFlightMessages` when LiteBus work needs a lower bound. See [Microsoft's Service Bus prefetch guidance](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-prefetch).

The root transport registers `transport.azure_service_bus.connectivity`. Configure an `AzureServiceBusQueueDiagnosticTarget` or `AzureServiceBusSubscriptionDiagnosticTarget` and grant listen permission on that entity. The probe uses [`PeekMessagesAsync`](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusreceiver.peekmessagesasync?view=azure-dotnet), which reads without locking or settling a message. Without a target, health reports degraded because constructing `ServiceBusClient` does not open a broker connection.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once when `CompleteMessage` follows successful accept | Exactly-once processing without idempotency |
| `AbandonMessage` on `ReturnToQueueAsync` | FIFO ordering unless using sessions explicitly |
| `DeliveryCount > 1` surfaces as `TransportMessage.Redelivered` | Cross-region failover |

Handler exceptions propagate to ingress, which applies `RequeueOnFailure` through `ReturnToQueueAsync` or `DiscardAsync`.

## Operations

| Symptom | Action |
| --- | --- |
| Messages stuck in peek-lock | Inspect accept failures; verify processor and store health |
| Emulator tests skip | Start Docker; see [Integration tests](../testing/integration-tests.md) |
| Repeated redelivery | Confirm idempotency keys on ingress metadata |
| `transport.azure_service_bus.connectivity` degraded | `ConnectivityCheckTarget` is missing | Configure a queue or topic subscription target |

## Tests

| Scenario | Location |
| --- | --- |
| Ingress end-to-end | `AzureServiceBusInboxIngressEndToEndIntegrationTests` |
| Poison and unknown contract drain | `AzureServiceBusInboxIngressFailureIntegrationTests` |
| Requeue on transient failure | `AzureServiceBusIngressRequeueBehaviorIntegrationTests` |

## Related Docs

* [Production runbook](../operations/runbook.md)
* [Reliable messaging](../reliable-messaging/README.md)
* [Integration tests](../testing/integration-tests.md)
