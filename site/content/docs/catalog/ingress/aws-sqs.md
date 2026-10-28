# AWS SQS Inbox Ingress

- **ID**: `ingress.aws-sqs`
- **Name**: AWS SQS inbox ingress
- **Maturity**: Beta
- **Summary**: This adapter polls an SQS queue and accepts messages into the inbox through shared transport ingress.

## Purpose and Scope

`UseAwsSqsIngress` registers `AwsSqsInboxIngressModule`. The module maps the queue URL, `ReceiveBatchSize`, the requeue policy, and the shared safety record into the transport ingress options. A root `AwsSqsTransportModule` supplies the SQS client.

SQS visibility timeout and delete-on-ack semantics are implemented in the transport consumer. Ingress applies shared accept-then-ack ordering on top. The enforced `Safety.MaxInFlightMessages` bound is 1.

## Beta Rationale

AWS SQS ingress has strong scenario coverage, including requeue enabled and disabled, but it remains Beta because:

- Broker option surface is intentionally narrower than AMQP.
- No contract tests yet assert telemetry or FIFO-specific semantics.

## Public Surface

```csharp
bus.AddAwsSqsTransport(sqsOptions);
bus.AddInbox(inbox => inbox.UseAwsSqsIngress(ingress =>
{
    ingress.UseOptions(new AwsSqsInboxIngressOptions
    {
        Destination = queueUrl,
        ReceiveBatchSize = 10,
        RequeueOnFailure = true,
        Safety = new TransportInboxIngressSafetyOptions
        {
            MaxInFlightMessages = 1
        }
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
| Native receive control | `ReceiveBatchSize`, 1 through 10 | `PrefetchCount`, 0 through 65535 |
| `RequeueOnFailure` toggle | yes (default true) | yes (default true) |
| Shared `Safety` settings | yes | yes |
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
- Contract resolution requires the standard LiteBus wire headers.
- The Feature Index lists this adapter as Beta tier.
- At composition, `ReceiveBatchSize` is validated from 1 through 10, matching the AWS SDK for .NET v4 `ReceiveMessageRequest` contract.
- Identity and idempotency default to broker-scoped values (`Safety.RequireStableIdentity=true`, `Safety.TrustApplicationHeaders=false`).

## Non-Goals

- SNS fan-out configuration (application wiring).
- FIFO ordering guarantees beyond SQS and transport adapter behavior.
- GA tier declaration.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | SQS delete-on-ack fails after inbox accept |

The meter is `LiteBus.Inbox`, registered through `AddLiteBusInboxMetrics()`.

### Transport Tracing and Metrics

| Signal | Broker tag | Registration |
| --- | --- | --- |
| `process {destination}` activity | `aws_sqs` | `AddLiteBusTransportInstrumentation()` |
| `litebus.transport.circuit_breaker.open` | `sqs` | Transport metrics from the root adapter |
| `litebus.transport.circuit_breaker.failure_count` | `sqs` | Same as above |

Visibility timeout and delete semantics belong to the transport layer. Ingress applies accept-then-ack ordering on top.

### Structured Logs

The consumer logs EventId 3002, 3003, and 3004 from `TransportInboxIngressLogMessages`.

## Identity, Idempotency, and Trusted Headers

SQS ingress uses shared mapper defaults:

| Setting | Default | Result |
| --- | --- | --- |
| `RequireStableIdentity` | true | A missing broker id fails closed |
| `Safety.TrustApplicationHeaders` | false | App idempotency, tenant, and visibility headers are ignored |
| Broker-scoped idempotency key | `ingress:{destination}:{brokerMessageId}` | Duplicate SQS deliveries map to the same inbox deduplication key when the id is stable |

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
- `ReceiveBatchSize = 10` under sustained LocalStack load.
- Broker-suite assertions for a missing contract version and a wrong CLR shape (covered in the InMemory suite).

### Out-of-Scope

- SNS fan-out configuration (application wiring).
- FIFO ordering guarantees beyond SQS and transport adapter behavior.
- GA tier declaration.

## References

- [AWS SQS transport](../../integrations/aws-sqs.md)
- [Feature Index: Ingress](../../reference/feature-index.md)
