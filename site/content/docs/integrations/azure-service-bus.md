# Azure Service Bus Transport

`LiteBus.Transport.AzureServiceBus` publishes and consumes through Azure Service Bus, using peek-lock receive for intake. LiteBus validates the adapter with the official Service Bus emulator in CI; live namespace soak is optional.

**Production tier: Beta**

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.AzureServiceBus` | `ITransportPublisher`, `AzureServiceBusConsumer`, connection options |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | Inbox processor publish to Service Bus |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | Outbox processor publish to Service Bus |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | Service Bus intake into `IInbox.AcceptAsync` |

Add inbox or outbox core and a storage package alongside the transport. Durable processing needs a store and a processor host; the transport alone persists nothing.

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

Register exactly one dispatcher per inbox or outbox: a second dispatcher registration throws `LiteBusConfigurationException`.

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `AzureServiceBusTransportOptions` | `ConnectionString` | required | Namespace or emulator connection |
| | `ClientId` | `null` | Client identifier passed to the Service Bus client |
| | `ConnectivityCheckTarget` | `null` | Queue or topic subscription peeked for readiness; missing reports degraded |
| | `ConsumerErrorRetryInterval` | 5 s | Processor restart backoff initial |
| | `ConsumerErrorRetryMaxInterval` | 60 s | Processor restart backoff cap |
| `AzureServiceBusInboxIngressOptions` | `Destination` | `""` (required) | Queue name, or topic name when `SubscriptionName` is set |
| | `SubscriptionName` | `null` | Subscription consumed when `Destination` names a topic |
| | `RequeueOnFailure` | `true` | Abandon vs complete on failure |
| | `PrefetchCount` | 0 | Messages eagerly cached by the Azure client |
| | `MaxConcurrentCalls` | `null` | Azure processor callback concurrency. `null` means the SDK default of 1. |
| | `Safety.MaxInFlightMessages` | `32` requested | Enforced with `MaxConcurrentCalls`: the bound is the lower of the two |

Azure defines `PrefetchCount` and `MaxConcurrentCalls` as separate processor settings. Keep the prefetch cache large enough to feed the requested callback concurrency, then use `Safety.MaxInFlightMessages` when LiteBus work needs a lower bound. See [Microsoft's Service Bus prefetch guidance](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-prefetch).

The root transport registers `transport.azure_service_bus.connectivity`. Configure a connectivity target and grant listen permission on that entity:

```csharp
new AzureServiceBusQueueDiagnosticTarget("orders-ingress")
new AzureServiceBusSubscriptionDiagnosticTarget("orders-events", "orders-ingress")
```

The queue target takes the queue name; the subscription target takes the topic name and the subscription name. The probe uses [`PeekMessagesAsync`](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusreceiver.peekmessagesasync?view=azure-dotnet), which reads without locking or settling a message. Without a target, health reports degraded because constructing `ServiceBusClient` does not open a broker connection.

## Ingress

`UseAzureServiceBusIngress` consumes `Destination` (a queue, or a topic when `SubscriptionName` is set) and accepts each message through `IInbox.AcceptAsync`. The registration sample above shows the full setup.

The message is completed only after the accept succeeds. On transient accept failure with `RequeueOnFailure = true`, `ReturnToQueueAsync` abandons the lock so the message is redelivered; with `RequeueOnFailure = false`, the message is completed and the failure drains as poison. Poison failures (unknown contract, oversize body, malformed payload) are never requeued. A message with `DeliveryCount > 1` surfaces as `TransportMessage.Redelivered`.

`Safety` carries the provider-neutral limits: `MaxMessageBytes` (default 4 MiB) rejects oversize bodies before deserialization, `RequireStableIdentity` (default `true`) requires a stable broker delivery id, `TrustApplicationHeaders` (default `false`) keeps application headers from overriding broker identity and tenant metadata, and `AuthorizeDeliveryAsync` optionally authorizes each delivery before accept. The enforced handler concurrency is the lower of `Safety.MaxInFlightMessages` and `MaxConcurrentCalls`.

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
| Emulator tests skip | Start Docker; see [Integration Tests](../testing/integration-tests.md) |
| Repeated redelivery | Confirm idempotency keys on ingress metadata |
| `transport.azure_service_bus.connectivity` degraded (missing target) | Configure a queue or topic subscription target |

## Tests

| Scenario | Location |
| --- | --- |
| Ingress end-to-end | `AzureServiceBusInboxIngressEndToEndIntegrationTests` |
| Poison and unknown contract drain | `AzureServiceBusInboxIngressFailureIntegrationTests` |
| Requeue on transient failure | `AzureServiceBusIngressRequeueBehaviorIntegrationTests` |

## Related Docs

* [Production Runbook](../operations/runbook.md)
* [Reliable Messaging](../reliable-messaging/README.md)
* [Integration Tests](../testing/integration-tests.md)
