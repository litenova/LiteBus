# AWS SQS Transport

- **ID**: `transport.aws-sqs`
- **Name**: AWS SQS transport
- **Maturity**: Beta
- **Summary**: Amazon SQS adapter with long-poll consume, visibility-timeout requeue, and canonical header mapping.

## What It Does

`AwsSqsTransportModule` composes `SqsPublisher` and `SqsConsumer` on `Amazon.SQS`. Publish targets queue URL in `Destination`; `Route` is stored as message attribute because SQS has no routing-key concept.

`SqsConsumer` reads `ApproximateReceiveCount` and uses `SqsRequeueBackoff` to calculate visibility timeout when handlers call `ReturnToQueueAsync`.

Beta tier note: this adapter is production-oriented but remains Beta while the project keeps broader broker soak coverage focused on AMQP.

## Public Surface

### Registration and Runtime Types

| API | Role |
| --- | --- |
| `AwsSqsTransportModule` | Registers SQS transport services |
| `SqsPublisher.PublishAsync` | Calls `SendMessage` |
| `SqsConsumer.StartAsync` | Runs long-poll receive loop |
| `SqsMessageMapper` | Maps request and received message attributes |
| `SqsRequeueBackoff` | Computes visibility timeout and poll backoff |
| `LiteBusTransportAwsTelemetry.MeterName` | Reserved SQS meter name |

### Options

| Property | Default | Purpose |
| --- | --- | --- |
| `Region` | `null` | AWS region |
| `ServiceUrl` | `null` | LocalStack or custom endpoint |
| `AccessKey` | `null` | Explicit credential key |
| `SecretKey` | `null` | Explicit credential secret |
| `LongPollWaitTimeSeconds` | `20` | Long-poll interval |
| `VisibilityTimeoutSeconds` | `30` | Receive visibility timeout |
| `RequeueVisibilityTimeoutSeconds` | `30` | Base requeue timeout |
| `MaxRequeueVisibilityTimeoutSeconds` | `900` | Max requeue timeout |
| `RequeueBackoffMultiplier` | `2.0` | Requeue timeout backoff multiplier |
| `PollBackoffInitial` | `500ms` | Batch failure poll delay |
| `PollBackoffMax` | `30s` | Max poll delay |
| `PollBackoffMultiplier` | `2.0` | Poll backoff multiplier |

### Ack Mapping

| Transport call | SQS action |
| --- | --- |
| `AcceptAsync` | `DeleteMessage` |
| `DiscardAsync` | `DeleteMessage` |
| `ReturnToQueueAsync` | `ChangeMessageVisibility` with computed timeout |

## Packages

- `LiteBus.Transport.AwsSqs`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`
- SQS queue URL and AWS credentials or LocalStack endpoint

## Invariants

- One transport broker adapter per process.
- Binary body payloads are base64 encoded with `litebus-content-encoding=base64`.
- Redelivery hint uses `ApproximateReceiveCount > 1`.

## Non-Goals

- SNS topology management.
- Automatic FIFO and dedup policy management.
- Cross-account IAM and queue policy automation.

## Observability

### Metrics

| Item | Value |
| --- | --- |
| Shared meter | `LiteBus.Transport` |
| Broker tag | `litebus.transport.broker="sqs"` |
| Shared gauges | `litebus.transport.circuit_breaker.open`, `litebus.transport.circuit_breaker.failure_count` |
| Reserved adapter meter | `LiteBus.Transport.AwsSqs` |
| OpenTelemetry registration | `AddLiteBusTransportMetrics()` |

### Tracing

- Activity source `LiteBus.Transport`
- Spans `send {destination}` and `process {destination}` with `messaging.system=aws_sqs`

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_ShouldRegisterTransportServices` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_ShouldMapBodyAndHeaders` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithBinaryBody_ShouldBase64Encode` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToTransportMessage_WithBase64Body_ShouldDecodeBytes` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ComputeRequeueVisibilityTimeout_shouldHonorReceiveCount` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |

### Untested

- Raw SQS publisher and consumer integration without durable wrappers.
- Gauge export assertions for broker tag `sqs`.
- Live AWS (non-LocalStack) end-to-end broker tests in this repo.

### Out-of-Scope

- SNS fan-out orchestration.
- Cross-account queue policy automation.

## Deep Docs

- [Aws-Sqs-Transport.md](../../integrations/aws-sqs.md)
- [Architecture.md](../../architecture/README.md)
- [manual-acknowledgement.md](manual-acknowledgement.md)
