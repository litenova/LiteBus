# Consumer Handler Invoker

- **ID**: `transport.consumer-handler-invoker`
- **Name**: Consumer handler invoker
- **Maturity**: GA
- **Summary**: Wrapper that runs raw transport handlers and requeues a delivery when an exception other than cancellation escapes the handler.

## What It Does

`TransportConsumerHandlerInvoker.InvokeAsync` runs a `TransportMessage` handler inside a guard that decides what happens to the delivery:

- It starts one `process {destination}` consumer activity around the handler.
- When the handler succeeds, the invoker does not acknowledge automatically.
- When the handler throws while the host is not stopping, the invoker calls `ReturnToQueueAsync`.
- When cancellation is requested for shutdown, `OperationCanceledException` propagates.

This behavior applies to raw `IMessageConsumer` hosts. Inbox ingress modules use their own ingestion policy and can discard poison messages based on validation and storage outcomes.

Consumer adapters keep the active consume task until `WaitUntilStoppedAsync` returns. Unexpected loop faults therefore reach the host supervision boundary instead of being reported only as a clean stop. Shutdown cancellation remains a normal completion path.

AMQP consumers copy the broker delivery body before invoking application code. RabbitMQ delivery buffers are owned by the client callback and must not be retained after the callback returns.

## Public Surface

| API | Role |
| --- | --- |
| `TransportConsumerHandlerInvoker.InvokeAsync(message, handler, cancellationToken)` | Executes handler with default requeue-on-failure behavior |
| `TransportConsumerHandlerInvoker.ShouldRequeueOnFailure(exception, cancellationToken)` | Distinguishes shutdown cancellation from retryable failure |

## Packages

- `LiteBus.Transport`

## Requires

- `transport.manual-acknowledgement`
- `transport.publish-consume-contracts`

## Invariants

- On the success path, the handler acknowledges explicitly with `AcceptAsync`, `DiscardAsync`, or `ReturnToQueueAsync`.
- Exceptions other than cancellation trigger a requeue at the invoker boundary.
- Shutdown cancellation does not trigger an explicit requeue from the invoker.
- Handler failures set error status and `error.type` on the process activity before requeue.

## Non-Goals

- Retry budgets and dead-letter policy.
- Message classification beyond cancellation versus non-cancellation exception.
- Replacing ingress-specific poison handling policy.

## Observability

- Activity source: `LiteBus.Transport`.
- Activity name: `process {destination}`.
- Activity kind: `Consumer`.
- Required attributes: `messaging.system`, `messaging.operation.name=process`, and `messaging.operation.type=process`.
- Optional attributes include destination, route, message id, conversation id, and redelivery state.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `HandlerThrow_ShouldRequeueMessageByDefault` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `ShouldNack_when_handlerFails_ShouldReturnTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `ShouldNack_when_shutdownCanceled_ShouldReturnFalse` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `StartConsumeActivity_ShouldRecordRabbitMqConsumerTags` | `LiteBus.Transport.UnitTests` |
| `StartConsumeActivity_WithSerializedTraceContext_ShouldContinueRemoteParent` | `LiteBus.Transport.UnitTests` |

### Untested

- Raw-host integration tests that assert invoker behavior on Kafka, SQS, and Service Bus adapters.

### Out-of-Scope

- Durable inbox poison and idempotency policy.
- Broker-native dead-letter automation.

## References

- [manual-acknowledgement.md](manual-acknowledgement.md)
- [Architecture.md](../../architecture/README.md)
- [ingress/README.md](../ingress/README.md)
