# Inbox AWS SQS Dispatch

## Header

- **ID**: `dispatch.inbox.aws-sqs`
- **Name**: Inbox AWS SQS dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Amazon SQS queue URLs through `UseAwsSqsDispatch`.

## What It Does

This extension registers `TransportInboxDispatchModule` with `AwsSqsTransportModule`. `DefaultDestination` is the queue URL. The shared transport inbox dispatcher publishes payload bytes and LiteBus headers as SQS message attributes.

Remote workers usually consume through SQS inbox ingress, or through custom consumers that accept messages into their inbox store.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.AwsSqs` | Registration glue |
| `LiteBus.Inbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.AwsSqs` | AWS SDK adapter |

## Requires

- `dispatch.transport-core`
- `transport.aws-sqs`
- `durable-core.inbox`

## Invariants

- SQS has no routing key. The resolved route travels as a message attribute instead.
- On the consumer side, a requeue changes the visibility timeout. The transport layer owns that behavior.
- A composition holds no more than one SQS transport module.

## Non-Goals

- It does not configure FIFO deduplication, because the application chooses its queues.
- It does not consume from SQS, because the ingress axis owns intake.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAwsSqsTransport(new AwsSqsTransportOptions
    {
        Region = "us-east-1",
        LongPollWaitTimeSeconds = 20
    });

    litebus.AddInbox(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseAwsSqsDispatch(
            options =>
            {
                options.DefaultDestination = "https://sqs.us-east-1.amazonaws.com/111122223333/orders";
            });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAwsSqsDispatch(Action<TransportInboxDispatcherOptions>)` | Registers the inbox transport dispatcher, which requires the root SQS transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | The shared dispatch publish path |

`AwsSqsTransportOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `Region` | `null` | AWS region |
| `ServiceUrl` | `null` | LocalStack or custom endpoint |
| `AccessKey` / `SecretKey` | `null` / `null` | Explicit credentials |
| `LongPollWaitTimeSeconds` | `20` | Consumer long poll wait |
| `VisibilityTimeoutSeconds` | `30` | Consumer visibility timeout |
| `RequeueVisibilityTimeoutSeconds` | `30` | Base requeue timeout |
| `MaxRequeueVisibilityTimeoutSeconds` | `900` | Max requeue timeout |
| `RequeueBackoffMultiplier` | `2.0` | Requeue backoff multiplier |
| `PollBackoffInitial` | `00:00:00.500` | Initial poll retry delay |
| `PollBackoffMax` | `00:00:30` | Max poll retry delay |
| `PollBackoffMultiplier` | `2.0` | Poll retry multiplier |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | The queue URL is the destination, with the message id and `messaging.system=aws_sqs` attributes |
| `litebus.transport.circuit_breaker.*` | Broker tag `sqs`. Outbox failure tests exercise the breaker, but no inbox dispatch test mirrors them |
| `litebus.inbox.processor.*` | The standard inbox processor metrics around dispatch |

SQS message attributes carry LiteBus headers mapped from envelope metadata.

## References

- [Integration-Tests.md](../../testing/integration-tests.md)
- [Architecture.md: Transport resilience matrix](../../architecture/README.md#transport-resilience-capability-matrix)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`) (LocalStack) |

### Untested

- Inbox dispatch failure for an invalid queue URL or invalid credentials.
- FIFO queue constraints and deduplication behavior.
- Route override branches through the resolver and the tenant strategy.

### Out-of-Scope

- FIFO deduplication configuration, which is an application queue choice
- Consuming from SQS (see `ingress.inbox.aws-sqs`)
