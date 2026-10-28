# Azure Service Bus Inbox Ingress

- **ID**: `ingress.azure-service-bus`
- **Name**: Azure Service Bus inbox ingress
- **Maturity**: Beta
- **Summary**: This adapter consumes Azure Service Bus queues or subscriptions and accepts messages into the inbox.

## Purpose and Scope

`UseAzureServiceBusIngress` registers `AzureServiceBusInboxIngressModule`. The module maps `AzureServiceBusInboxIngressOptions` into the shared transport ingress options and requires a root `AzureServiceBusTransportModule`. It registers the shared handler and consumer loop. Queue destinations use `Destination`, while topic destinations also set `SubscriptionName`.

When `RequeueOnFailure` is true, failed store writes abandon messages for retry, using Service Bus retry semantics through the transport adapter. The enforced handler bound is the lower of `Safety.MaxInFlightMessages` and `MaxConcurrentCalls`, which defaults to the SDK value of 1.

## Beta Rationale

Service Bus ingress is Beta because the ingress flow is stable but its test matrix covers less than the AMQP matrix:

- Emulator ingress tests have no duplicate MessageId matrix or full header edge-case matrix.
- No tests cover the requeue-off poison drain matrix.
- Broker option surface is intentionally narrower than AMQP.

## Public Surface

```csharp
bus.AddAzureServiceBusTransport(serviceBusOptions);
bus.AddInbox(inbox => inbox.UseAzureServiceBusIngress(ingress =>
{
    ingress.UseOptions(new AzureServiceBusInboxIngressOptions
    {
        Destination = "commands-inbox",
        SubscriptionName = "orders-worker",
        PrefetchCount = 10,
        MaxConcurrentCalls = 4,
        RequeueOnFailure = true,
        Safety = new TransportInboxIngressSafetyOptions
        {
            MaxInFlightMessages = 4
        }
    });
}));
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAzureServiceBusIngress(Action<AzureServiceBusInboxIngressModuleBuilder>)` | Registration extension |
| `AzureServiceBusInboxIngressModuleBuilder.UseOptions(AzureServiceBusInboxIngressOptions)` | Queue or topic path and ingress behavior |
| `AzureServiceBusInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without processor loop |
| `AzureServiceBusInboxIngressModule` | Child module |

## Azure Options and Parity vs AMQP

| Capability | Azure Service Bus ingress | AMQP ingress |
| --- | --- | --- |
| Destination required | `Destination` required | `QueueName` required |
| Root transport required | `AddAzureServiceBusTransport(...)` | `AddAmqpTransport(...)` |
| Prefetch setting | yes | yes |
| Named topic subscription | yes | no |
| Separate native handler concurrency | yes (`MaxConcurrentCalls`) | no |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| Shared `Safety` settings | yes | yes |
| Declare destination knobs | no | yes |

## Packages

- `LiteBus.Inbox.Ingress.AzureServiceBus`
- `LiteBus.Transport.AzureServiceBus` (explicit root transport)

## Requires

- `ingress.registration`
- Shared ingress capabilities
- `transport.azure-service-bus`
- Inbox storage and contracts

## Invariants

- `Destination` and root `AddAzureServiceBusTransport(...)` are required at compose time.
- When `Destination` is a topic, set `SubscriptionName`.
- `PrefetchCount` fills the Azure client cache, while `MaxConcurrentCalls` controls Azure processor callbacks. `Safety.MaxInFlightMessages` applies the final LiteBus handler cap.
- LiteBus contract headers are required on the wire payload.
- The Feature Index lists this adapter as Beta tier, so applications must test broker-specific edge cases.
- Identity and idempotency default to broker-scoped values (`Safety.RequireStableIdentity=true`, `Safety.TrustApplicationHeaders=false`).

## Non-Goals

- GA tier declaration.
- AMQP or Kafka ingress in the same package.
- Built-in Service Bus dead-letter administration.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | Complete/abandon fails after inbox accept |

The meter is `LiteBus.Inbox`, registered through `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Broker tag | Registration |
| --- | --- | --- |
| `process {destination}` activity | `servicebus` | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.open` | `azure_service_bus` | Transport metrics from the root adapter |
| `litebus.transport.circuit_breaker.failure_count` | `azure_service_bus` | Same as above |

### Structured Logs

The shared consumer logs EventId 3002, 3003, and 3004 from `TransportInboxIngressLogMessages`.

## Identity, Idempotency, and Trusted Headers

Service Bus ingress uses shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | A missing broker id fails closed |
| `Safety.TrustApplicationHeaders` | false | App idempotency, tenant, and visibility headers are ignored |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | Duplicate deliveries are absorbed when the delivery id is stable |

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishThroughServiceBus_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `UnknownContract_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `InvalidJson_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `StoreFull_ShouldDrainQueueAndKeepPrefilledRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/AzureServiceBus/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- Header edge cases on the Service Bus emulator (missing contract name, wrong version, invalid message id).
- `RequeueOnFailure = false` poison message drain.
- Prefetch and lock duration tuning under load.
- Duplicate MessageId idempotency matrix for ingress on the emulator.
- Ack-failed-after-accept metric assertion in the emulator suite.

### Out-of-Scope

- GA tier declaration.
- AMQP or Kafka ingress in the same package.
- Built-in Service Bus dead-letter administration.

## References

- [Azure Service Bus transport](../../integrations/azure-service-bus.md)
- [Feature Index: Ingress](../../reference/feature-index.md)
