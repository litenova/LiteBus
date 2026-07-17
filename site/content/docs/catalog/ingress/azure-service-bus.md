# Azure Service Bus Inbox Ingress

- **ID**: `ingress.azure-service-bus`
- **Name**: Azure Service Bus inbox ingress
- **Maturity**: Beta
- **Summary**: Consumes Azure Service Bus queues or subscriptions and accepts messages into the inbox.

## Purpose and Scope

`UseAzureServiceBusIngress` registers `AzureServiceBusInboxIngressModule`. The module maps `AzureServiceBusInboxIngressOptions` into shared transport ingress options, requires a root `AzureServiceBusTransportModule`, and registers the shared handler and consumer loop. Queue destinations use `Destination`; topic destinations also set `SubscriptionName`.

Failed store writes can abandon messages for retry when `RequeueOnFailure` is true (Service Bus retry semantics via transport adapter).

## Beta Rationale

Service Bus ingress is Beta because ingress flow is stable but matrix depth is narrower than AMQP:

- Duplicate MessageId and full header edge-case matrices are missing in emulator ingress tests.
- Requeue-off poison drain matrix is not covered.
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
        MaxConcurrentMessages = 4,
        RequeueOnFailure = true
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
| Separate handler concurrency limit | yes (`MaxConcurrentMessages`) | no |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| `TrustApplicationHeaders` exposure on broker options | no | yes |
| Batch accept knobs on broker options | no | yes |
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
- `SubscriptionName` is required when `Destination` is a topic.
- `MaxConcurrentMessages` controls callback concurrency independently from `PrefetchCount`.
- LiteBus contract headers are required on the wire payload.
- Beta tier per v6 feature index; treat broker edge cases as application-tested.
- Identity and idempotency default to broker-scoped values (`RequireStableIdentity=true`, `TrustApplicationHeaders=false`).

## Non-Goals

- GA tier declaration.
- AMQP or Kafka ingress in the same package.
- Built-in Service Bus dead-letter administration.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | Complete/abandon fails after inbox accept |

Meter `LiteBus.Inbox` via `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Broker tag | Registration |
| --- | --- | --- |
| `process {destination}` activity | `servicebus` | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.open` | `azure_service_bus` | Transport metrics from the root adapter |
| `litebus.transport.circuit_breaker.failure_count` | `azure_service_bus` | same |

### Structured Logs

EventId 3002, 3003, 3004 from `TransportInboxIngressLogMessages` on shared consumer.

## Identity, Idempotency, and Trusted Headers

Service Bus ingress uses shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | Missing broker id fails closed |
| `TrustApplicationHeaders` | false | App idempotency and tenant headers are ignored |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | Stable duplicate absorption when delivery id is stable |

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

- Header edge cases (missing contract name, wrong version, invalid message id) on Service Bus emulator.
- `RequeueOnFailure = false` poison message drain.
- Prefetch and lock duration tuning under load.
- Duplicate MessageId idempotency matrix for ingress on emulator.
- Ack-failed-after-accept metric assertion in emulator suite.

### Out-of-Scope

- GA tier declaration.
- AMQP or Kafka ingress in the same package.
- Built-in Service Bus dead-letter administration.

## Deep Docs

- [Azure Service Bus transport](../../integrations/azure-service-bus.md)
- [v6 feature index: Ingress](../../reference/feature-index-v6.md)
