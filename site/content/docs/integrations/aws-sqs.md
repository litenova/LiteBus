# AWS SQS Transport

**Production tier: Beta**

`LiteBus.Transport.AwsSqs` provides Amazon SQS publish and long-poll consume. LiteBus validates adapters with LocalStack in CI; live AWS account soak is not a GA gate.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.AwsSqs` | `ITransportPublisher`, `SqsConsumer`, `AwsSqsTransportOptions` |
| `LiteBus.Inbox.Dispatch.AwsSqs` | Inbox processor publish to SQS |
| `LiteBus.Outbox.Dispatch.AwsSqs` | Outbox processor publish to SQS |
| `LiteBus.Inbox.Ingress.AwsSqs` | SQS intake into `IInbox.AcceptAsync` |

## Registration

```csharp
var sqsOptions = new AwsSqsTransportOptions
{
    Region = "us-east-1",
    // LocalStack:
    ServiceUrl = "http://localhost:4566",
    AccessKey = "test",
    SecretKey = "test"
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

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `AwsSqsTransportOptions` | `LongPollWaitTimeSeconds` | 20 | Receive wait time |
| | `VisibilityTimeoutSeconds` | 30 | Initial visibility on receive |
| | `RequeueVisibilityTimeoutSeconds` | 30 | Base timeout on nack/requeue |
| | `MaxRequeueVisibilityTimeoutSeconds` | 900 | Backoff cap |
| | `RequeueBackoffMultiplier` | 2.0 | Uses `ApproximateReceiveCount` when present |
| | `PollBackoffInitial` / `PollBackoffMax` | 500 ms / 30 s | Full-batch failure poll delay |
| `AwsSqsInboxIngressOptions` | `RequeueOnFailure` | `true` | Change visibility vs delete on failure |
| | `ReceiveBatchSize` | 1 | Messages requested per receive call; valid range is 1 through 10 |
| | `Safety.MaxInFlightMessages` | 32 | Concurrent LiteBus handler cap |

`ReceiveBatchSize` maps directly to `ReceiveMessageRequest.MaxNumberOfMessages`. Module composition rejects values outside the [AWS SDK for .NET v4 range of 1 through 10](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TReceiveMessageRequest.html) instead of silently clamping them.

## Wire Encoding

SQS message bodies are strings. UTF-8 JSON and text payloads are sent as plain message bodies. Binary payloads are base64-encoded with a `litebus-content-encoding: base64` message attribute. Correlation identifiers use the canonical `correlation-id` header (`TransportHeaders.CorrelationId`); legacy `CorrelationId` attributes are accepted on ingress only.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once when delete follows successful accept | FIFO ordering unless using FIFO queues explicitly |
| Exponential visibility timeout on requeue | Exactly-once processing |
| Binary-safe bodies via base64 encoding | Arbitrary binary without encoding attribute on legacy publishers |
| Poison drain when `RequeueOnFailure = false` | Cross-region failover |

Handler exceptions propagate to ingress, which applies `RequeueOnFailure` through `ReturnToQueueAsync` or `DiscardAsync`. The SQS consumer does not auto-requeue on handler throw.

## Operations

| Symptom | Action |
| --- | --- |
| Messages invisible too long after failure | Lower `RequeueVisibilityTimeoutSeconds` for faster retry; check DLQ policy |
| Poll stalls after repeated failures | Inspect `PollBackoffMax`; fix store/accept errors |
| LocalStack tests skip | Start Docker; see [Integration tests](../testing/integration-tests.md) |

## Tests

| Scenario | Location |
| --- | --- |
| Mapper and visibility backoff | `LiteBus.Transport.AwsSqs.UnitTests` |
| Requeue on transient accept failure | `AwsSqsIngressRequeueBehaviorIntegrationTests` |
| Poison drain | Same suite, `RequeueDisabled_*` |

## Related Docs

* [Production runbook](../operations/runbook.md)
* [Reliable messaging](../reliable-messaging/README.md)
