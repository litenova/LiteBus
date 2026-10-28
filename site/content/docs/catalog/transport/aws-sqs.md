# AWS SQS Transport

- **ID**: `transport.aws-sqs`
- **Name**: AWS SQS transport
- **Maturity**: Beta
- **Summary**: Amazon SQS adapter with long-poll consume, visibility-timeout requeue, and canonical header mapping.

## What It Does

`AwsSqsTransportModule` composes `SqsPublisher` and `SqsConsumer` on `Amazon.SQS`. Publish targets queue URL in `Destination`; `Route` is stored as message attribute because SQS has no routing-key concept.

`SqsConsumer` reads `ApproximateReceiveCount` and uses `SqsRequeueBackoff` to calculate visibility timeout when handlers call `ReturnToQueueAsync`. The publisher is a singleton and consumers are transient.

Beta tier note: this adapter is production-oriented but remains Beta while the project keeps broader broker soak coverage focused on AMQP.

## FIFO Queues

On queue URLs ending in `.fifo`, the mapper copies `TransportPublishRequest.MessageGroupId` to `MessageGroupId` and `TransportPublishRequest.DeduplicationKey` to `MessageDeduplicationId`; standard queues carry neither field. Outbox dispatch fills both on every publish: the group is the envelope tenant id, otherwise the contract name, and the deduplication key is the envelope idempotency key, otherwise the message id.

## Public Surface

### Registration and Runtime Types

| API | Role |
| --- | --- |
| `AwsSqsTransportModule` | Registers SQS transport services |
| `SqsPublisher.PublishAsync` | Calls `SendMessage` |
| `SqsConsumer.StartAsync` | Runs long-poll receive loop |
| `SqsMessageMapper` | Maps request and received message attributes |
| `SqsRequeueBackoff` | Computes visibility timeout and poll backoff |
| `AwsSqsConnectivityDiagnosticCheck` | Reads one queue ARN for host readiness |
| `LiteBusTransportAwsTelemetry.MeterName` | Reserved SQS meter name |

### Options

| Property | Default | Purpose |
| --- | --- | --- |
| `Region` | `null` | AWS region |
| `ServiceUrl` | `null` | LocalStack or custom endpoint |
| `AccessKey` | `null` | Explicit credential key |
| `SecretKey` | `null` | Explicit credential secret |
| `ConnectivityCheckQueueUrl` | `null` | Queue URL read by the readiness probe; missing reports degraded |
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
- SQS-safe UTF-8 text is sent as a plain body. Invalid UTF-8 and code points outside the SQS body ranges are base64 encoded with `litebus-content-encoding=base64`.
- SQS allows ten message attributes. When route, content type, durable metadata, and caller headers would exceed that limit, non-reserved headers are serialized into the `litebus-headers` attribute and expanded on receive.
- Mapper-owned attributes such as `litebus-content-encoding`, `litebus-message-id`, `correlation-id`, `litebus-route`, and `content-type` override conflicting caller values.
- Redelivery hint uses `ApproximateReceiveCount > 1`.
- A message without system attributes counts as a first delivery when requeue backoff is computed.
- A received message whose body cannot be decoded or whose packed headers are malformed is deleted as poison and skipped, so one bad delivery cannot stall the loop; the delete is best-effort and a failed delete leaves the message for redelivery.
- `StopAsync` completes without throwing when the consume loop faulted; the terminal failure propagates from `WaitUntilStoppedAsync` before and after `StopAsync`.
- FIFO fields apply only on `.fifo` queue URLs; a standard queue ignores `MessageGroupId` and `DeduplicationKey`.

## Non-Goals

- SNS topology management.
- FIFO queue provisioning and content-based deduplication policy management.
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

### Diagnostics

- Diagnostic check id: `transport.sqs.connectivity`
- The probe requests only `QueueArn` from `ConnectivityCheckQueueUrl` through `GetQueueAttributes`.
- Grant `sqs:GetQueueAttributes` on the target queue. Missing target configuration reports degraded.
- See the [AWS SDK for .NET v4 SQS client contract](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TSQSClient.html).

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_ShouldRegisterTransportServices` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_PublisherIsSingletonAndConsumerIsTransient` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_FifoQueueUrl_SetsDeduplicationAndGroupIds` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_StandardQueueUrl_OmitsDeduplicationAndGroupIds` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_FifoQueueUrlWithoutFifoFields_LeavesIdsUnset` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `CheckAsync_WithoutQueueUrl_ShouldReturnDegraded` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_ShouldMapBodyAndHeaders` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithBinaryBody_ShouldBase64Encode` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithInvalidUtf8Body_ShouldBase64EncodeWithoutReplacingBytes` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithDisallowedControlByte_ShouldBase64Encode` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithContentEncodingHeaderAndTextBody_ShouldRemoveStaleMarker` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithReservedCallerHeaders_ShouldPreferMapperValues` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_WithFullDurableMetadata_ShouldPackHeadersAndRoundTripThem` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToTransportMessage_WithBase64Body_ShouldDecodeBytes` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ComputeRequeueVisibilityTimeout_shouldHonorReceiveCount` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
| `PublishThroughSqs_ShouldAcceptProcessAndDispatchCommand` readiness assertion | `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`) |
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
