# Inbox AWS SQS Dispatch

## Header

- **ID**: `dispatch.inbox.aws-sqs`
- **Name**: Inbox AWS SQS dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Amazon SQS queue URLs through `UseAwsSqsDispatch`.

## What It Does

Registers `TransportInboxDispatchModule` with `AwsSqsTransportModule`. `DefaultDestination` is the queue URL. Shared transport inbox dispatcher publishes payload bytes and LiteBus headers as SQS message attributes.

Remote workers typically consume via SQS inbox ingress or custom consumers that accept into their inbox store.

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

- SQS has no routing key; route resolver affects attribute mapping where applicable.
- Requeue on consumer side uses visibility timeout change (transport layer).
- One SQS transport module per process.

## Non-Goals

- Does not configure FIFO deduplication (application queue choice).
- Does not consume from SQS (ingress axis).

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
| `InboxModuleBuilder.UseAwsSqsDispatch(Action<TransportInboxDispatcherOptions>)` | Registers inbox transport dispatcher that requires the root SQS transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Shared dispatch publish path |

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
| `send {destination}` | Queue URL as destination; message id and `messaging.system=aws_sqs` attributes |
| `litebus.transport.circuit_breaker.*` | Broker tag `sqs` (outbox failure tests exercise breaker; inbox dispatch failure mirror absent) |
| `litebus.inbox.processor.*` | Standard inbox processor metrics around dispatch |

SQS message attributes carry LiteBus headers mapped from envelope metadata.

## Deep Docs

- [Integration-Tests.md](../../testing/integration-tests.md)
- [Architecture.md: Transport resilience matrix](../../architecture/README.md#transport-resilience-capability-matrix)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxModuleBuilderAwsDispatchExtensions_should_expose_use_aws_sqs_dispatch` | `LiteBus.Inbox.UnitTests` (`Dispatch/AwsSqs/`) |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/AwsSqs/`) (LocalStack) |

### Untested

- Inbox dispatch failure for invalid queue URL or credentials.
- FIFO queue constraints and deduplication behavior.
- Route override branches through resolver and tenant strategy.

### Out-of-Scope

- FIFO deduplication configuration (application queue choice)
- Consuming from SQS (`ingress.inbox.aws-sqs`)
