# Ingress Safety and Authorization Options

- **ID**: `ingress.safety-options`
- **Name**: Ingress safety and authorization options
- **Maturity**: GA
- **Summary**: Configures provider-neutral body, identity, authorization, admission, and batch limits for every ingress adapter.

## Purpose and Scope

Every broker-specific ingress record exposes one `TransportInboxIngressSafetyOptions` value through its `Safety` property. The adapter preserves that record when it creates `TransportInboxIngressOptions`. Broker destinations and native receive controls remain on their adapter records because those settings do not share semantics.

For example, an SQS service can request 10 messages per receive call while keeping LiteBus handler admission at one:

```csharp
ingress.UseOptions(new AwsSqsInboxIngressOptions
{
    Destination = queueUrl,
    ReceiveBatchSize = 10,
    Safety = new TransportInboxIngressSafetyOptions
    {
        MaxInFlightMessages = 1,
        MaxMessageBytes = 1024 * 1024
    }
});
```

## Option Model

| Type / member | Default | Role |
| --- | --- | --- |
| `TransportInboxIngressOptions.Destination` | required | Queue, topic, or channel name |
| `TransportInboxIngressOptions.PrefetchCount` | 0 | Native RabbitMQ or Azure Service Bus prefetch only |
| `TransportInboxIngressOptions.ReceiveBatchSize` | 1 | SQS messages requested per receive call only |
| `TransportInboxIngressOptions.MaxConcurrentCalls` | null | Native Azure Service Bus callback concurrency only |
| `TransportInboxIngressOptions.SubscriptionName` | null | Named subscription when a destination is a topic |
| `TransportInboxIngressOptions.DeclareDestination` | false | Declare an AMQP queue before subscribing |
| `TransportInboxIngressOptions.DurableDestination` | false | Make a declared AMQP queue durable |
| `TransportInboxIngressOptions.RequeueOnFailure` | true | Requeue transient accept failures |
| `TransportInboxIngressOptions.Safety` | default safety record | Provider-neutral ingress safety settings |
| `Safety.MaxMessageBytes` | 4 MiB | Reject oversized bodies before deserialize; zero disables the limit |
| `Safety.RequireStableIdentity` | true | Fail when broker delivery id is missing |
| `Safety.TrustApplicationHeaders` | false | Honor application idempotency, tenant, and message id headers |
| `Safety.AuthorizeDeliveryAsync` | null | Host callback before deserialize and accept |
| `Safety.MaxInFlightMessages` | 32 | Cap concurrent LiteBus delivery handlers |
| `Safety.EnableBatchAccept` | false | Enable buffered inbox batch accepts |
| `Safety.BatchSize` | 10 | Deliveries stored in one inbox batch |
| `Safety.BatchMaxWait` | 200 ms | Partial batch flush delay |

Each ingress module calls `Safety.Validate()` during composition. Negative message limits or wait durations, zero batch sizes, and zero maximum-in-flight values fail before the host starts. AWS also rejects `ReceiveBatchSize` outside 1 through 10. Azure rejects negative prefetch and non-positive `MaxConcurrentCalls`.

## Broker Option Parity

| Broker options type | Shared `Safety` | Native receive control | Destination declaration |
| --- | --- | --- | --- |
| `AmqpInboxIngressOptions` | yes | `PrefetchCount` | queue declare and durable flags |
| `KafkaInboxIngressOptions` | yes | none; the consume loop reads one record at a time | no |
| `AwsSqsInboxIngressOptions` | yes | `ReceiveBatchSize` | no |
| `AzureServiceBusInboxIngressOptions` | yes | `PrefetchCount`, `MaxConcurrentCalls` | no |
| `InMemoryInboxIngressOptions` | yes | none; capacity belongs to the transport module | no |

## Public Surface

- `TransportInboxIngressOptions`, the runtime bridge record
- `TransportInboxIngressSafetyOptions`, the provider-neutral safety record
- One broker-specific ingress options record in each adapter package

## Packages

- `LiteBus.Inbox.Ingress`
- Broker option records in each `LiteBus.Inbox.Ingress.*` package

## Requires

- `ingress.registration` to register options in DI

## Invariants

- `Safety.AuthorizeDeliveryAsync` exceptions follow the same requeue and discard path as store failures.
- `Safety.MaxMessageBytes` is checked before deserialization.
- `Safety.TrustApplicationHeaders` stays false unless the broker binding authenticates upstream publishers.
- `Safety.RequireStableIdentity=true` and broker message ids provide deterministic duplicate absorption across retries.
- `Safety.MaxInFlightMessages` is independent from broker prefetch, receive batch size, and native callback concurrency.

## Non-Goals

- Authentication framework integration. The host supplies `AuthorizeDeliveryAsync`.
- Per-tenant broker bindings.
- Store retention or dead-letter tuning.

## Observability

| Option violation | Observable signal |
| --- | --- |
| `Safety.MaxMessageBytes` exceeded | Exception before store; consumer discards or requeues per `ingress.ack-policy` |
| Missing broker id when stable identity is required | `InboxIngressException` from the mapper |
| `Safety.AuthorizeDeliveryAsync` rejection | The same ack policy path as a store failure |
| Invalid numeric bound | Composition exception before background services start |

## Test Coverage

| Test method | Project |
| --- | --- |
| `AcceptAsync_WhenBodyExceedsMaxMessageBytes_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `AcceptAsync_WithAuthorizationCallback_ShouldAuthorizeBeforeStoreWrite` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `HandleDeliveryAsync_WhenAuthorizationRejects_ShouldDiscardWithoutAccept` | `LiteBus.Inbox.UnitTests` (`Ingress/`) |
| `InboxIngressOptions_ShouldPreserveSafetyAndNativeConsumerSettings` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `AwsSqsIngress_WithInvalidReceiveBatchSize_ShouldRejectComposition` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `AzureServiceBusIngress_WithInvalidMaxConcurrentCalls_ShouldRejectComposition` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `InMemoryIngress_WithInvalidMaxInFlightMessages_ShouldRejectComposition` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

Live broker authorization rejection coverage remains limited to AMQP. Shared mapping and composition propagation are covered for all five ingress builders.

## Deep Docs

- [Inbox ingress options](../../reliable-messaging/inbox.md)
- [Batch accept](batch-accept.md)
- [Publish and consume contracts](../transport/publish-consume-contracts.md)
