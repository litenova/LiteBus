# Outbox Processor

**ID:** `durable-core.outbox-processor`
**Maturity:** GA

## Summary

The LiteBus outbox processor is a background worker that leases due outbox envelopes, publishes them through `IOutboxDispatcher`, and records published, retry, or dead-letter state.

## What It Does

`PipelinedOutboxProcessor` mirrors the inbox pipeline for publication. It leases pending rows and dispatches each envelope to the in-process event mediator or an external broker. It persists terminal state immediately per envelope, and it supports concurrency, heartbeat, and hook policies. Because dispatch runs before terminal persist, a crash between the two leaves a known at-least-once publication window.

## Public Surface

### Consumer Contracts

- **`IOutboxProcessor`**: This is the processor contract for manual passes and testing. It exposes `ProcessPendingAsync`.
- **`PipelinedOutboxProcessor`**: This is the default pipelined implementation. It uses lease heartbeat and concurrent publication workers.
- **`IOutboxProcessorControl`**: This is the contract that exposes pause, drain, and resume for operational control.
- **`OutboxProcessorOptions`**: Use this to configure batch size, lease duration, heartbeat interval, retry policy, dispatcher concurrency, and hook failure policy.
- **`OutboxProcessorHostOptions`**: Use this to configure the background loop poll interval.

### Invocation

- **`ProcessPendingAsync(CancellationToken)`**: Call this to run one processor pass that leases a batch, publishes each envelope, and persists terminal state.
- **`PauseAsync` / `DrainAsync` / `ResumeAsync`**: Call these to pause, drain, and resume the processor through `IOutboxProcessorControl`.
- The background service invokes passes until host shutdown.

In a typical deployment, the application enqueues domain events through `IOutbox`, and the processor publishes them asynchronously to in-process handlers or external brokers.

### Registration

- **`EnableOutboxProcessor(host => ...)`** registers the manifest background service.
- **`UseProcessorOptions(OutboxProcessorOptions)`** configures batch, lease, retry, and hook policy.
- **`Use*Storage()`** and exactly one **`Use*Dispatch()`** are required in the same outbox builder. Callback order is irrelevant.
- Transport dispatch adapters may expose **`ValidatePayloadBeforeDispatch`** on dispatcher options as a pre-dispatch deserialization gate.

### Configuration

- **`OutboxProcessorOptions`** has the same lease, retry, and concurrency shape as inbox. **`HookFailurePolicy`** is `null` by default, which means the dispatcher recommendation (`IOutboxDispatcherModule.DefaultHookFailurePolicy`). An explicit value always takes precedence.
- **Transport dispatchers** recommend `HookFailurePolicy = CompleteDespiteHookFailure`, because the broker send already succeeded.
- **In-process dispatch** inherits the `DeadLetter` default.
- **`ValidatePayloadBeforeDispatch`**: When enabled on transport options, the dispatcher deserializes before broker publish.
- **`InProcessOutboxDispatchOptions.RequireHandler`** defaults to `true` and is set through `UseInProcessDispatch(dispatch => ...)`. A dispatched message with no handler fails the attempt, retries, and moves to dead letter. When it is `false`, the message is marked published without any handler having run.

Set `Retry.IsPermanentFailure` to move application failures that can never succeed on retry to dead letter. See [Permanent Failures](lease-retry-dead-letter.md#permanent-failures).

### Extension Points

- **`IOutboxDispatcher`**: Register exactly one dispatcher: the in-process event mediator, AMQP, Kafka, or another transport dispatcher.
- **`IProcessorEnvelopeHook`** (`LiteBus.Messaging.Abstractions.Processing`): These are the pre- and post-publication hooks (see the processor-hooks capability).
- Custom storage implements lease and state writer roles.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox` | Processor, factory, background service |
| `LiteBus.Outbox.Abstractions` | `OutboxProcessorOptions` |
| `LiteBus.Outbox.Dispatch.*` | Required dispatcher |

## Requires

- Register storage and exactly one dispatcher.
- Provide event handlers for in-process dispatch, or broker configuration for transport dispatch.
- Register the contract for each stored event type.

## Invariants

- Publication is at-least-once. A crash after broker ack may republish on retry.
- An unset `HookFailurePolicy` resolves to the dispatcher recommendation. Transport dispatchers report `CompleteDespiteHookFailure`, and in-process dispatch inherits `DeadLetter`.
- In-process dispatch requires a handler by default (`RequireHandler = true`). A message with no handler fails the attempt instead of being marked published.
- `ValidatePayloadBeforeDispatch` on transport options controls pre-dispatch deserialization.
- Batch enqueue uses parallel serialization in the envelope factory. That is a writer concern, not a processor concern.

## Non-Goals

- This capability does not persist a two-phase broker ack token before terminal state.
- This capability does not provide synchronous enqueue-and-publish.
- This capability does not provide cross-broker fan-out from one envelope without dispatcher logic.

## Observability

Outbox processor instrumentation uses meter `LiteBus.Outbox` (`LiteBusOutboxTelemetry.MeterName`) and activity source `LiteBus.Outbox`. Register with `AddLiteBusOutboxMetrics()` and `AddLiteBusOutboxInstrumentation()` from `LiteBus.Outbox.Extensions.OpenTelemetry`. The [OpenTelemetry Metric Catalog](../../architecture/README.md#opentelemetry-metric-catalog) lists every public instrument name with its meter, constant, and description. The table below says only what each one tells an operator.

| Instrument | What it tells you |
|---|---|
| `litebus.outbox.processor.state` | This is recorded on scrape when `IOutboxProcessorControl` is registered. Use it during broker maintenance or controlled drain. |
| `litebus.outbox.processor.published` | This is recorded for each envelope marked published in a pass. The count may exceed business events when at-least-once republish occurs after a crash. |
| `litebus.outbox.processor.failed` | This is recorded for each envelope marked failed (retry scheduled). Rising counts correlate with broker outages or serialization errors. |
| `litebus.outbox.processor.dead_lettered` | This is recorded for each envelope moved to dead letter. Dead-lettered envelopes require operator requeue or manual inspection. |
| `litebus.outbox.processor.loop_errors` | This is recorded when the background loop catches an unhandled exception. Investigate store connectivity and unexpected pass failures. |
| `litebus.outbox.processor.leases_acquired` | This records the envelopes leased at pass start. Compare it to the pending queue depth gauge. |
| `litebus.outbox.processor.lease_lost` | This is recorded when lease renewal failure cancels publication. Long broker publish calls need a lease duration and heartbeat interval longer than the call. |
| `litebus.outbox.processor.persist_skipped`, `litebus.outbox.processor.persist_rejected` | These are recorded when terminal persist is skipped after lease loss, which may follow a successful broker publish. The skip is the source of at-least-once republish (see `InMemoryOutboxAtLeastOnceIntegrationTests`). |
| `litebus.outbox.processor.persist_failed` | This is recorded when terminal persist throws after a successful broker publish. The exception is logged and the pass continues. The row may republish on retry, so consumers must deduplicate. |
| `litebus.outbox.diagnostics.unavailable` | This is recorded when the queue depth diagnostic probe fails against the backing store. A rising counter with a flat `litebus.outbox.queue.depth` suggests probe failures. |
| `litebus.outbox.processor.dispatch_duration` | This is recorded after each publication attempt completes. Broker latency informs lease duration tuning. |
| `litebus.outbox.queue.depth` | This is recorded on periodic diagnostics scrape grouped by status. Rising pending depth with a flat publishing rate signals a publication backlog. |

## References

- [Outbox](../../reliable-messaging/outbox.md) (processing vs publishing, hook policy)
- [Hosted services](../../architecture/hosted-services.md)
- [Outbox AMQP dispatch](../../integrations/outbox-amqp-dispatch.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Outbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

### Covered Use Cases

#### `PipelinedOutboxProcessorTests.PipelinedProcessor_WithConcurrencyOne_ShouldPublishAllMessages`

- **Use case**: When concurrency is one, a single worker publishes all pending messages
- **Test kind**: Unit
- **Description**: In-memory outbox processor
- **Behavior**: `ProcessPendingAsync` with `DispatcherConcurrency = 1`
- **Expected outcome**: All rows reach published status
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_ShouldDispatchThroughMockDispatcherAndMarkPublished`

- **Use case**: When dispatch succeeds, the row is marked published through the test dispatcher
- **Test kind**: Unit
- **Description**: Mock dispatcher success path
- **Behavior**: `ProcessPendingAsync` with successful dispatch
- **Expected outcome**: The row reaches published status
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_ShouldSupportClosedGenericIntegrationEvents`

- **Use case**: When a closed generic event type is stored, dispatch resolves and publishes successfully
- **Test kind**: Unit
- **Description**: Closed generic contract registration and dispatch
- **Behavior**: Enqueue and process a closed generic event
- **Expected outcome**: The event is published successfully
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_WhenDispatcherThrows_ShouldMarkFailedAndSetVisibleAfter`

- **Use case**: When the dispatcher throws, the row is marked failed with retry visibility
- **Test kind**: Unit
- **Description**: A dispatcher that throws
- **Behavior**: Dispatch exception during pass
- **Expected outcome**: The row has failed status and a future `visible_after`
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_WhenDispatcherExceedsMaxAttempts_ShouldMoveToDeadLetter`

- **Use case**: When retry attempts are exhausted, the row moves to dead letter
- **Test kind**: Unit
- **Description**: Repeated dispatch failures
- **Behavior**: Failures repeat until max attempts
- **Expected outcome**: The row reaches DeadLettered status
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxProcessorControlTests.PauseAsync_should_block_processing_until_resume`

- **Use case**: When paused, no publications occur until resume
- **Test kind**: Unit
- **Description**: Processor pause API
- **Behavior**: `PauseAsync` during pending work
- **Expected outcome**: Nothing is published while paused
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxProcessorControlTests.DrainAsync_should_process_pending_messages_and_complete`

- **Use case**: When drain is signaled, pending messages are published and the processor completes draining
- **Test kind**: Unit
- **Description**: Graceful drain
- **Behavior**: `DrainAsync` with pending rows
- **Expected outcome**: The pending rows are cleared
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxHostingTests.ProcessorBackgroundService_ShouldPublishScheduledMessages`

- **Use case**: When the hosted loop runs, scheduled messages are published
- **Test kind**: Unit
- **Description**: Background service wiring
- **Behavior**: `OutboxProcessorBackgroundService.ExecuteAsync`
- **Expected outcome**: The events are published
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxHostingTests.ProcessorBackgroundService_WhenMissingDispatcher_ShouldThrowOnBuild`

- **Use case**: When the processor is enabled without a dispatcher, module build fails
- **Test kind**: Unit
- **Description**: Registration guard
- **Behavior**: `EnableOutboxProcessor` without a dispatch adapter
- **Expected outcome**: The build throws an exception
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_enabled_should_throw_before_publish`

- **Use case**: When payload validation is enabled, an invalid payload fails before broker publish
- **Test kind**: Unit
- **Description**: `ValidatePayloadBeforeDispatch` gate
- **Behavior**: Dispatch with invalid serialized payload
- **Expected outcome**: The dispatch throws before publish, and no broker message is sent.
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_disabled_should_publish_without_deserializing`

- **Use case**: When payload validation is disabled, publish proceeds without pre-dispatch deserialization
- **Test kind**: Unit
- **Description**: Fast path without validation
- **Behavior**: Dispatch with validation disabled
- **Expected outcome**: Broker publish without a deserialization step
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `InMemoryOutboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry`

- **Use case**: When terminal persist is skipped after successful publish, retry republishes the message
- **Test kind**: Integration
- **Description**: At-least-once publication window
- **Behavior**: Simulated persist skip after broker ack
- **Expected outcome**: The retry publishes a second time
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`)

#### `OutboxProcessorCorrectnessTests.PipelinedProcessor_when_hook_failure_policy_is_complete_despite_hook_failure_should_mark_published`

- **Use case**: When the hook failure policy tells the processor to complete despite hook failure, publication succeeds despite the hook error
- **Test kind**: Unit
- **Description**: Transport default hook policy
- **Behavior**: Post-dispatch hook throws with complete-despite-failure policy
- **Expected outcome**: The row reaches published status despite the hook error
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `PostgreSqlOutboxEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughPostgreSqlStore`

- **Use case**: When the outbox runs against PostgreSQL, the processor publishes events to completion
- **Test kind**: Integration
- **Description**: Real PostgreSQL store E2E
- **Behavior**: Enqueue followed by a processor pass
- **Expected outcome**: The row is published in the database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxProcessorEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore`

- **Use case**: When the outbox runs against EF Core, the processor publishes events and handlers receive them
- **Test kind**: Integration
- **Description**: EF Core store E2E
- **Behavior**: Full enqueue and process cycle
- **Expected outcome**: The handler receives the event, and the row is published.
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue`

- **Use case**: When AMQP dispatch is configured, the processor publishes the envelope to the broker queue
- **Test kind**: Integration
- **Description**: AMQP outbox dispatch
- **Behavior**: The test processes pending rows through the AMQP dispatcher
- **Expected outcome**: The message arrives on the AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `InProcessOutboxDispatcherTests.ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished`

- **Use case**: When in-process dispatch is configured, the event mediator invokes handlers and marks the row published
- **Test kind**: Unit
- **Description**: In-process outbox dispatcher
- **Behavior**: `ProcessPendingAsync` with in-process dispatcher
- **Expected outcome**: The event handler is invoked, and the row reaches published status.
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `OutboxProcessorLoopErrorTelemetryTests.ExecuteAsync_when_processor_throws_should_increment_loop_errors_counter`

- **Use case**: When the processor throws unexpectedly in the background loop, the loop error counter increments
- **Test kind**: Unit
- **Description**: Pass-level exception in hosted loop
- **Behavior**: A simulated processor throw
- **Expected outcome**: `litebus.outbox.processor.loop_errors` is incremented
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `InProcessOutboxDispatchRequireHandlerTests.A_message_with_no_handler_is_not_marked_published`

- **Use case**: When in-process dispatch finds no handler, the attempt fails instead of being marked published
- **Test kind**: Unit
- **Description**: Default `RequireHandler = true` with no registered handler
- **Behavior**: `ProcessPendingAsync` on a handlerless envelope
- **Expected outcome**: The row has failed status with retry and is never silently published.
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

#### `InProcessOutboxDispatchRequireHandlerTests.Turning_RequireHandler_off_restores_the_silent_no_op`

- **Use case**: When `RequireHandler` is disabled, a handlerless dispatch completes
- **Test kind**: Unit
- **Description**: `UseInProcessDispatch(dispatch => dispatch.RequireHandler = false)`
- **Behavior**: `ProcessPendingAsync` on a handlerless envelope
- **Expected outcome**: The row reaches published status without any handler having run
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Two-phase broker ack before terminal persist | No | Not supported | N/A | N/A |
| Cross-broker fan-out from one envelope | Yes | Only a single dispatcher is supported, so fan-out is app logic | N/A | N/A |
| Synchronous enqueue-and-publish API | No | The writer and processor are separate | N/A | N/A |

### Out-of-Scope Use Cases

- Two-phase broker acknowledgment token persisted before terminal state
- Multi-dispatcher load balancing within one module
- Guaranteed exactly-once broker delivery
