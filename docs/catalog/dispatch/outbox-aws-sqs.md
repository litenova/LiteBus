# Outbox AWS SQS Dispatch

## Header

- **ID**: `dispatch.outbox.aws-sqs`
- **Name**: Outbox AWS SQS dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Amazon SQS with LiteBus headers via `UseAwsSqsDispatch`.

## What It Does

Registers `TransportOutboxDispatchModule` with `AwsSqsTransportModule`. Processor publication sends integration event payloads to the configured queue URL. Topic metadata on outbox envelopes influences routing attributes where the shared dispatcher applies it.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.AwsSqs` | Registration glue |
| `LiteBus.Outbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.AwsSqs` | AWS SDK adapter |

## Requires

- `dispatch.transport-core`
- `transport.aws-sqs`
- `durable-core.outbox`

## Invariants

- Default hook failure policy: `CompleteDespiteHookFailure`.
- DeleteMessage on ack happens on consumer/ingress side, not on dispatch publish.
- At-least-once downstream delivery expected.

## Non-Goals

- Does not manage IAM policies or queue creation.
- Does not support SNS fan-out directly (publish to SQS only).

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAwsSqsTransport(new AwsSqsTransportOptions
    {
        Region = "us-east-1",
        LongPollWaitTimeSeconds = 20
    });

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseAwsSqsDispatch(
            options =>
            {
                options.DefaultDestination = "https://sqs.us-east-1.amazonaws.com/111122223333/events";
                options.ResolveRoute = envelope => envelope.Topic ?? envelope.ContractName;
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseAwsSqsDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers outbox transport dispatcher that requires the root SQS transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | Defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Shared outbox publish logic |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | SQS send activity with queue URL, message id, and `messaging.system=aws_sqs` |
| `litebus.transport.circuit_breaker.open` / `failure_count` | Tag `sqs`; exercised in failure integration tests |
| `litebus.outbox.processor.failed` | Row marked failed with `VisibleAfter` when broker unreachable |
| `litebus.outbox.processor.published` | Success after send and terminal persist |

## Deep Docs

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Integration-Tests.md](../../testing/integration-tests.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToSqsQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoute` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/AwsSqs/`) |

### Untested

- Crash after successful SQS publish but before terminal persist.
- After-dispatch hook failure branch with `CompleteDespiteHookFailure`.
- SNS fan-out behavior, which is outside current SQS dispatcher package scope.

### Out-of-Scope

- IAM policies and queue provisioning
- SNS fan-out (SQS publish only)
