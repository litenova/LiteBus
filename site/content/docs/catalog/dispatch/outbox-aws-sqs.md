# Outbox AWS SQS Dispatch

## Header

- **ID**: `dispatch.outbox.aws-sqs`
- **Name**: Outbox AWS SQS dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to Amazon SQS with LiteBus headers via `UseAwsSqsDispatch`.

## What It Does

This extension registers `TransportOutboxDispatchModule` with `AwsSqsTransportModule`. The processor publishes integration event payloads to the configured queue URL. Topic metadata on outbox envelopes influences the routing attributes where the shared dispatcher applies it.

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

- The default hook failure policy is `CompleteDespiteHookFailure`.
- The DeleteMessage acknowledgment happens on the consumer or ingress side, never on dispatch publish.
- Expect at-least-once downstream delivery.
- FIFO queues (queue URLs ending in `.fifo`) carry `MessageDeduplicationId` and `MessageGroupId` from the publish request `DeduplicationKey` (the envelope idempotency key, otherwise the message id) and `MessageGroupId` (the envelope tenant id, otherwise the contract name). Standard queues carry neither.

## Non-Goals

- It does not manage IAM policies or queue creation.
- It does not support SNS fan-out directly. It publishes to SQS only.

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
| `OutboxModuleBuilder.UseAwsSqsDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers the outbox transport dispatcher, which requires the root SQS transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | The policy defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | The shared outbox publish logic |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | SQS send activity with the queue URL, the message id, and `messaging.system=aws_sqs` |
| `litebus.transport.circuit_breaker.open` / `failure_count` | Tag `sqs`. The failure integration tests exercise it |
| `litebus.outbox.processor.failed` | The processor marks the row failed with `VisibleAfter` when the broker is unreachable |
| `litebus.outbox.processor.published` | Success after the send and the terminal persist |

## References

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
| `ToSendMessageRequest_FifoQueueUrl_SetsDeduplicationAndGroupIds` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_StandardQueueUrl_OmitsDeduplicationAndGroupIds` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `ToSendMessageRequest_FifoQueueUrlWithoutFifoFields_LeavesIdsUnset` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `PublishAsync_WithDuplicateDeduplicationKey_DeliversTwoMessagesInOrder` | `LiteBus.Transport.IntegrationTests` (`Aws/`, LocalStack) |

### Untested

- A crash after a successful SQS publish but before the terminal persist.
- The after-dispatch hook failure branch with `CompleteDespiteHookFailure`.
- SNS fan-out behavior, which is outside the current SQS dispatcher package scope.

### Out-of-Scope

- IAM policies and queue provisioning
- SNS fan-out (SQS publish only)
