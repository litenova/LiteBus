# AWS SQS Inbox Ingress

- **ID**: `ingress.aws-sqs`
- **Name**: AWS SQS inbox ingress
- **Maturity**: Beta
- **Summary**: Polls an SQS queue and accepts messages into the inbox through shared transport ingress.

## Purpose and Scope

`UseAwsSqsIngress` registers `AwsSqsInboxIngressModule`. The module maps `AwsSqsInboxIngressOptions` (queue URL destination, prefetch batch size, requeue) into shared transport ingress options, requires a root `AwsSqsTransportModule`, and registers the shared handler and consumer.

SQS visibility timeout and delete-on-ack semantics are implemented in the transport consumer; ingress applies shared accept-then-ack ordering on top.

## Beta Rationale

AWS SQS ingress has strong scenario coverage, including requeue on and off, but it remains Beta because:

- Broker option surface is intentionally narrower than AMQP.
- Telemetry assertions and FIFO-specific semantics are not yet covered as contract tests.

## Public Surface

```csharp
bus.AddAwsSqsTransport(sqsOptions);
bus.AddInbox(inbox => inbox.UseAwsSqsIngress(ingress =>
{
    ingress.UseOptions(new AwsSqsInboxIngressOptions
    {
        Destination = queueUrl,
        PrefetchCount = 10,
        RequeueOnFailure = true
    });
}));
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAwsSqsIngress(Action<AwsSqsInboxIngressModuleBuilder>)` | Registration extension |
| `AwsSqsInboxIngressModuleBuilder.UseOptions(AwsSqsInboxIngressOptions)` | Queue URL and ingress behavior |
| `AwsSqsInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without poll loop |
| `AwsSqsInboxIngressModule` | Child module |

## AWS Options and Parity vs AMQP

| Capability | AWS SQS ingress | AMQP ingress |
| --- | --- | --- |
| Destination required | `Destination` (queue URL) required | `QueueName` required |
| Root transport required | `AddAwsSqsTransport(...)` | `AddAmqpTransport(...)` |
| Prefetch setting | yes | yes |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| `TrustApplicationHeaders` exposure on broker options | no | yes |
| Batch accept knobs on broker options | no | yes |
| Declare destination knobs | no | yes |

## Packages

- `LiteBus.Inbox.Ingress.AwsSqs`
- `LiteBus.Transport.AwsSqs` (explicit root transport)

## Requires

- `ingress.registration`
- Shared ingress capabilities
- `transport.aws-sqs`
- Inbox storage and contracts

## Invariants

- `Destination` (queue URL) and root `AddAwsSqsTransport(...)` are required at compose time.
- Standard LiteBus wire headers required for contract resolution.
- Beta tier per v6 feature index.
- Identity and idempotency default to broker-scoped values (`RequireStableIdentity=true`, `TrustApplicationHeaders=false`).

## Non-Goals

- SNS fan-out configuration (application wiring).
- FIFO ordering guarantees beyond SQS and transport adapter behavior.
- GA tier declaration.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | SQS delete-on-ack fails after inbox accept |

Meter `LiteBus.Inbox` via `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Broker tag | Registration |
| --- | --- | --- |
| `process {destination}` activity | `aws_sqs` | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.open` | `sqs` | Transport metrics from the root adapter |
| `litebus.transport.circuit_breaker.failure_count` | `sqs` | same |

Visibility timeout and delete semantics are transport-layer; ingress applies accept-then-ack ordering on top.

### Structured Logs

EventId 3002, 3003, 3004 from `TransportInboxIngressLogMessages`.

## Identity, Idempotency, and Trusted Headers

SQS ingress uses shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | Missing broker id fails closed |
| `TrustApplicationHeaders` | false | App idempotency and tenant headers are ignored |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | Duplicate SQS deliveries map to same inbox dedup key when id is stable |

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `UnknownContract_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `InvalidJson_ShouldNotWriteToStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `StoreFull_ShouldDrainQueueAndKeepPrefilledRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `RequeueDisabled_WithPoisonMessage_ShouldDrainQueue` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `DuplicateMessageId_ShouldCreateSingleInboxRow` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `MissingContractName_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `WrongContractVersion_ShouldDiscardWithoutStoreWrite` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `InvalidMessageId_ShouldAcceptWithGeneratedInboxId` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- SQS FIFO ordering and deduplication semantics.
- Delete-on-ack failure with `ingress.ack_failed_after_accept` metric assertion.
- High `PrefetchCount` receive batch sizing under LocalStack load.
- Broker-suite assertion for missing contract version and wrong CLR shape (covered in InMemory suite).

### Out-of-Scope

- SNS fan-out configuration (application wiring).
- FIFO ordering guarantees beyond SQS and transport adapter behavior.
- GA tier declaration.

## Deep Docs

- [AWS SQS transport](../../integrations/aws-sqs.md)
- [v6 feature index: Ingress](../../reference/feature-index-v6.md)
