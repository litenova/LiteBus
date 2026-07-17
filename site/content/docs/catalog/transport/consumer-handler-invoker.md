# Consumer Handler Invoker

- **ID**: `transport.consumer-handler-invoker`
- **Name**: Consumer handler invoker
- **Maturity**: GA
- **Summary**: Wrapper that executes raw transport handlers and requeues deliveries when non-cancellation exceptions escape.

## What It Does

`TransportConsumerHandlerInvoker.InvokeAsync` runs a `TransportMessage` handler in a guarded boundary:

- it starts one `process {destination}` consumer activity around the handler;
- if handler succeeds, invoker does not auto-ack;
- if handler throws while host is not stopping, invoker calls `ReturnToQueueAsync`;
- if cancellation is requested for shutdown, `OperationCanceledException` propagates.

This behavior applies to raw `IMessageConsumer` hosts. Inbox ingress modules use their own ingestion policy and can discard poison messages based on validation and storage outcomes.

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

- Success path requires explicit ack by handler (`AcceptAsync`, `DiscardAsync`, or `ReturnToQueueAsync`).
- Non-cancellation exceptions trigger requeue at invoker boundary.
- Shutdown cancellation does not trigger explicit requeue from invoker.
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

## Deep Docs

- [manual-acknowledgement.md](manual-acknowledgement.md)
- [Architecture.md](../../architecture/README.md)
- [ingress/README.md](../ingress/README.md)
