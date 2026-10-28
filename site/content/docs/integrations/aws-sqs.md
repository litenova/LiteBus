# AWS SQS Transport

LiteBus connects to Amazon SQS through `LiteBus.Transport.AwsSqs`, which publishes and consumes with long-poll receive for intake. LiteBus validates the adapter with LocalStack in CI. A soak test against a live AWS account is not required before GA.

**Production tier: Beta**

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.AwsSqs` | `ITransportPublisher`, `SqsConsumer`, `AwsSqsTransportOptions` |
| `LiteBus.Inbox.Dispatch.AwsSqs` | Inbox processor publish to SQS |
| `LiteBus.Outbox.Dispatch.AwsSqs` | Outbox processor publish to SQS |
| `LiteBus.Inbox.Ingress.AwsSqs` | SQS intake into `IInbox.AcceptAsync` |

Install inbox or outbox core and a storage package alongside the transport. Durable processing needs a store and a processor host. The transport alone persists nothing.

## Registration

```csharp
var sqsOptions = new AwsSqsTransportOptions
{
    Region = "us-east-1",
    // LocalStack:
    ServiceUrl = "http://localhost:4566",
    AccessKey = "test",
    SecretKey = "test",
    ConnectivityCheckQueueUrl = queueUrl
};

builder.AddAwsSqsTransport(sqsOptions);
builder.AddMessaging(_ => { });
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UseInMemoryStorage(); // or PostgreSQL in production
    inbox.UseAwsSqsDispatch(_ => { });
    inbox.UseAwsSqsIngress(ingress =>
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
    });
});
```

Register exactly one dispatcher per inbox or outbox: a second dispatcher registration throws `LiteBusConfigurationException`.

When both `Region` and `ServiceUrl` are `null`, the AWS SDK resolves the region from environment variables, the shared configuration file, or instance metadata. When `ServiceUrl` is set, it takes precedence over `Region`, and no AWS region resolution is required. A failure to resolve a region surfaces as `AwsSqsTransportConfigurationException` without recording a circuit-breaker failure.

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `AwsSqsTransportOptions` | `Region` | `null` | AWS region system name such as `us-east-1`. `null` falls back to the SDK resolution chain unless `ServiceUrl` is set. |
| | `ServiceUrl` | `null` | Custom endpoint for LocalStack or VPC endpoints. Takes precedence over `Region`. |
| | `AccessKey` | `null` | Explicit credential. `null` uses the SDK credential chain. |
| | `SecretKey` | `null` | Explicit credential. `null` uses the SDK credential chain. |
| | `LongPollWaitTimeSeconds` | 20 | Receive wait time |
| | `ConnectivityCheckQueueUrl` | `null` | Queue whose ARN is read for readiness. Without a target, health reports degraded. |
| | `VisibilityTimeoutSeconds` | 30 | Initial visibility on receive |
| | `RequeueVisibilityTimeoutSeconds` | 30 | Base timeout on nack/requeue |
| | `MaxRequeueVisibilityTimeoutSeconds` | 900 | Backoff cap |
| | `RequeueBackoffMultiplier` | 2.0 | Uses `ApproximateReceiveCount` when present |
| | `PollBackoffInitial` / `PollBackoffMax` | 500 ms / 30 s | Full-batch failure poll delay |
| | `PollBackoffMultiplier` | 2.0 | Multiplier per consecutive full-batch failure |
| `AwsSqsInboxIngressOptions` | `Destination` | `""` (required) | Queue URL the ingress consumer receives from |
| | `RequeueOnFailure` | `true` | Change visibility vs delete on failure |
| | `ReceiveBatchSize` | 1 | Messages requested per receive call. Valid range is 1 through 10. |
| | `Safety.MaxInFlightMessages` | `32` requested | The enforced bound is 1, because the SQS adapter runs one handler at a time. |

`ReceiveBatchSize` maps directly to `ReceiveMessageRequest.MaxNumberOfMessages`. Module composition rejects values outside the [AWS SDK for .NET v4 range of 1 through 10](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TReceiveMessageRequest.html) instead of silently clamping them.

While a handler runs, the consumer extends visibility once every half of `VisibilityTimeoutSeconds`, so a long handler keeps its receipt handle alive.

The root transport registers `transport.sqs.connectivity`. Configure `ConnectivityCheckQueueUrl` and grant `sqs:GetQueueAttributes` on that queue. The probe requests only `QueueArn` through the current [AWS SDK for .NET v4 SQS client](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TSQSClient.html). Without a target, health reports degraded because creating an SDK client does not verify broker access.

## Ingress

`UseAwsSqsIngress` long-polls the `Destination` queue URL and accepts each message through `IInbox.AcceptAsync`. The registration sample above shows the full setup.

`ReceiveBatchSize` controls how many messages each receive call requests (1 through 10). The message is deleted only after the accept succeeds. On transient accept failure with `RequeueOnFailure = true`, `ReturnToQueueAsync` changes the message visibility with exponential backoff (`RequeueVisibilityTimeoutSeconds`, `RequeueBackoffMultiplier`, `MaxRequeueVisibilityTimeoutSeconds`). With `RequeueOnFailure = false`, the message is deleted and the failure drains as poison. Poison failures (unknown contract, oversize body, malformed payload) are never requeued.

`Safety` carries the provider-neutral limits:

- `MaxMessageBytes` (default 4 MiB) rejects oversize bodies before deserialization.
- `RequireStableIdentity` (default `true`) requires a stable broker delivery id.
- `TrustApplicationHeaders` (default `false`) keeps application headers from overriding broker identity and tenant metadata.
- `AuthorizeDeliveryAsync` optionally authorizes each delivery before accept.

The enforced handler concurrency is 1, so raising `Safety.MaxInFlightMessages` alone does not parallelize the SQS adapter.

A message without SQS system attributes counts as a first delivery when requeue backoff is computed. When the consume loop faults, `StopAsync` completes without throwing and the terminal failure propagates from `WaitUntilStoppedAsync`.

## Wire Encoding

SQS message bodies are strings. LiteBus uses strict UTF-8 decoding and the SQS Unicode ranges before sending a plain body. Invalid UTF-8 and disallowed control bytes are base64-encoded with a `litebus-content-encoding: base64` message attribute, preserving the original bytes. Correlation identifiers use the canonical `correlation-id` header (`TransportHeaders.CorrelationId`).

SQS permits ten message attributes per message. LiteBus keeps mapper-owned route, content type, identifier, correlation, and encoding attributes separate. When the complete header set would exceed ten attributes, the remaining headers are serialized into `litebus-headers`. Ingress expands that attribute before contract mapping.

Some received messages can never be processed: a body that cannot be decoded (an invalid body with the `base64` marker) or a malformed packed `litebus-headers` attribute. The consumer deletes such a message and continues with the next delivery instead of faulting the loop.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once when delete follows successful accept | FIFO ordering unless using FIFO queues explicitly |
| Exponential visibility timeout on requeue | Exactly-once processing |
| Binary-safe bodies via base64 encoding | Arbitrary binary without encoding attribute on legacy publishers |
| Poison drain when `RequeueOnFailure = false` | Cross-region failover |

Handler exceptions propagate to ingress, which applies `RequeueOnFailure` through `ReturnToQueueAsync` or `DiscardAsync`. The SQS consumer does not requeue automatically when a handler throws.

## Operations

| Symptom | Action |
| --- | --- |
| Messages invisible too long after failure | Lower `RequeueVisibilityTimeoutSeconds` for faster retry and check the DLQ policy |
| Poll stalls after repeated failures | Inspect `PollBackoffMax` and fix store and accept errors |
| LocalStack tests skip | Start Docker; see [Integration Tests](../testing/integration-tests.md) |
| `transport.sqs.connectivity` degraded (missing target) | Configure `ConnectivityCheckQueueUrl` with narrow attribute-read permission |

## Tests

| Scenario | Location |
| --- | --- |
| Mapper and visibility backoff | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| Requeue on transient accept failure | `AwsSqsIngressRequeueBehaviorIntegrationTests` |
| Poison drain | Same suite, `RequeueDisabled_*` |

## Related Docs

* [Production Runbook](../operations/runbook.md)
* [Reliable Messaging](../reliable-messaging/README.md)
