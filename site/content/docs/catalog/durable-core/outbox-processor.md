# Outbox Processor

**ID:** `durable-core.outbox-processor`
**Maturity:** GA

## Summary

Background worker that leases due outbox envelopes, publishes through `IOutboxDispatcher`, and records published, retry, or dead-letter state.

## What It Does

`PipelinedOutboxProcessor` mirrors the inbox pipeline for publication. It leases pending rows, dispatches to in-process event mediator or external broker, persists terminal state immediately per envelope, and supports concurrency, heartbeat, and hook policies. Dispatch occurs before terminal persist, creating a known at-least-once publication window on crash.

## Public Surface

### Consumer Contracts

- **`IOutboxProcessor`**: Processor contract (`ProcessPendingAsync`) for manual passes and testing.
- **`PipelinedOutboxProcessor`**: Default pipelined implementation with lease heartbeat and concurrent publication workers.
- **`IOutboxProcessorControl`**: Pause, drain, and resume for operational control.
- **`OutboxProcessorOptions`**: Batch size, lease duration, heartbeat interval, retry policy, dispatcher concurrency, and hook failure policy.
- **`OutboxProcessorHostOptions`**: Background loop poll interval.

### Invocation

- **`ProcessPendingAsync(CancellationToken)`**: One processor pass: lease batch, publish each envelope, persist terminal state.
- **`PauseAsync` / `DrainAsync` / `ResumeAsync`**: Operational control on `IOutboxProcessorControl`.
- Background service invokes passes until host shutdown.

Typical deployment: application enqueues domain events via `IOutbox`; processor publishes asynchronously to in-process handlers or external brokers.

### Registration

- **`EnableOutboxProcessor(host => ...)`** registers manifest background service.
- **`UseProcessorOptions(OutboxProcessorOptions)`** configures batch, lease, retry, and hook policy.
- **`Use*Storage()`** and exactly one **`Use*Dispatch()`** are required in the same outbox builder; callback order is irrelevant.
- Transport dispatch adapters may expose **`ValidatePayloadBeforeDispatch`** on dispatcher options (pre-dispatch deserialization gate).

### Configuration

- **`OutboxProcessorOptions`**: Same lease/retry/concurrency shape as inbox; **`HookFailurePolicy`** defaults differ by dispatcher kind.
- **Transport dispatchers**: Default `HookFailurePolicy = CompleteDespiteHookFailure`.
- **In-process dispatch**: Default `HookFailurePolicy = DeadLetter`.
- **`ValidatePayloadBeforeDispatch`**: When enabled on transport options, dispatcher deserializes before broker publish.

### Extension Points

- **`IOutboxDispatcher`**: Exactly one dispatcher (in-process event mediator, AMQP, Kafka, and similar).
- **`IOutboxProcessorEnvelopeHook`**: Pre/post publication hooks (see processor-hooks capability).
- Custom storage implements lease and state writer roles.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox` | Processor, factory, background service |
| `LiteBus.Outbox.Abstractions` | `OutboxProcessorOptions` |
| `LiteBus.Outbox.Dispatch.*` | Required dispatcher |

## Requires

- Storage and exactly one dispatcher registered
- Event handlers (in-process) or broker configuration (transport dispatch)
- Contract registration for stored event types

## Invariants

- Publication is at-least-once; crash after broker ack may republish on retry
- Transport dispatchers default `HookFailurePolicy = CompleteDespiteHookFailure`
- In-process dispatch defaults `HookFailurePolicy = DeadLetter`
- `ValidatePayloadBeforeDispatch` on transport options controls pre-dispatch deserialization
- Batch enqueue uses parallel serialization in envelope factory (writer concern, not processor)

## Non-Goals

- Two-phase broker ack token persisted before terminal state
- Synchronous enqueue-and-publish
- Cross-broker fan-out from one envelope without dispatcher logic

## Observability

Outbox processor instrumentation uses meter `LiteBus.Outbox` (`LiteBusOutboxTelemetry.MeterName`) and activity source `LiteBus.Outbox`. Register with `AddLiteBusOutboxMetrics()` and `AddLiteBusOutboxInstrumentation()` from `LiteBus.Outbox.Extensions.OpenTelemetry`.

### `litebus.outbox.processor.state`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Scrape when `IOutboxProcessorControl` is registered
- **Value**: `0` Running, `1` Paused, `2` Draining
- **Operational note**: Use during broker maintenance or controlled drain

### `litebus.outbox.processor.published`

- **Kind**: Counter
- **When emitted**: Each envelope marked published in a pass
- **Operational note**: May exceed business events when at-least-once republish occurs after crash

### `litebus.outbox.processor.failed`

- **Kind**: Counter
- **When emitted**: Each envelope marked failed (retry scheduled)
- **Operational note**: Correlates with broker outages or serialization errors

### `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: Each envelope moved to dead letter
- **Operational note**: Requires operator requeue or manual inspection

### `litebus.outbox.processor.loop_errors`

- **Kind**: Counter
- **When emitted**: Background loop unhandled exception
- **Operational note**: Investigate store connectivity and unexpected pass failures

### `litebus.outbox.processor.leases_acquired`

- **Kind**: Counter (`LiteBusOutboxTelemetry.ProcessorLeasesAcquiredInstrumentName`)
- **When emitted**: Envelopes leased at pass start
- **Operational note**: Compare to pending queue depth gauge

### `litebus.outbox.processor.lease_lost`

- **Kind**: Counter (`LiteBusOutboxTelemetry.ProcessorLeaseLostInstrumentName`)
- **When emitted**: Lease renewal failure cancels publication
- **Operational note**: Long broker publish calls need adequate lease and heartbeat

### `litebus.outbox.processor.persist_skipped` / `litebus.outbox.processor.persist_rejected`

- **Kind**: Counter
- **When emitted**: Terminal persist skipped after lease loss (may follow successful broker publish)
- **Operational note**: Source of at-least-once republish; see `InMemoryOutboxAtLeastOnceIntegrationTests`

### `litebus.outbox.processor.persist_failed`

- **Kind**: Counter (`LiteBusOutboxTelemetry.ProcessorPersistFailedInstrumentName`)
- **When emitted**: Terminal persist throws after successful broker publish (exception is logged; pass continues)
- **Operational note**: Row may republish on retry; consumers must deduplicate

### `litebus.outbox.diagnostics.unavailable`

- **Kind**: Counter (`LiteBusOutboxTelemetry.DiagnosticsUnavailableInstrumentName`)
- **When emitted**: Queue depth diagnostic probe fails against the backing store
- **Operational note**: Rising counter with flat `litebus.outbox.queue.depth` suggests probe failures

### `litebus.outbox.processor.dispatch_duration`

- **Kind**: Histogram in milliseconds (`LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName`)
- **When emitted**: After each publication attempt completes
- **Operational note**: Broker latency informs lease duration tuning

### `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic diagnostics scrape grouped by status
- **Tags**: `litebus.outbox.status`
- **Operational note**: Rising pending with flat publishing rate signals publication backlog

## Deep Docs

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
- **Description**: Mock dispatcher happy path
- **Behavior**: `ProcessPendingAsync` with successful dispatch
- **Expected outcome**: Published status
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_ShouldSupportClosedGenericIntegrationEvents`

- **Use case**: When a closed generic event type is stored, dispatch resolves and publishes successfully
- **Test kind**: Unit
- **Description**: Closed generic contract registration and dispatch
- **Behavior**: Enqueue and process closed generic event
- **Expected outcome**: Published successfully
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_WhenDispatcherThrows_ShouldMarkFailedAndSetVisibleAfter`

- **Use case**: When the dispatcher throws, the row is marked failed with retry visibility
- **Test kind**: Unit
- **Description**: Throwing dispatcher
- **Behavior**: Dispatch exception during pass
- **Expected outcome**: Failed status and future `visible_after`
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxTests.ProcessPendingAsync_WhenDispatcherExceedsMaxAttempts_ShouldMoveToDeadLetter`

- **Use case**: When retry attempts are exhausted, the row moves to dead letter
- **Test kind**: Unit
- **Description**: Repeated dispatch failures
- **Behavior**: Failures until max attempts
- **Expected outcome**: DeadLettered status
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxProcessorControlTests.PauseAsync_should_block_processing_until_resume`

- **Use case**: When paused, no publications occur until resume
- **Test kind**: Unit
- **Description**: Processor pause API
- **Behavior**: `PauseAsync` during pending work
- **Expected outcome**: No publishes while paused
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxProcessorControlTests.DrainAsync_should_process_pending_messages_and_complete`

- **Use case**: When drain is signaled, pending messages are published and the processor completes draining
- **Test kind**: Unit
- **Description**: Graceful drain
- **Behavior**: `DrainAsync` with pending rows
- **Expected outcome**: Pending cleared
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxHostingTests.ProcessorBackgroundService_ShouldPublishScheduledMessages`

- **Use case**: When the hosted loop runs, scheduled messages are published
- **Test kind**: Unit
- **Description**: Background service wiring
- **Behavior**: `OutboxProcessorBackgroundService.ExecuteAsync`
- **Expected outcome**: Events published
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `OutboxHostingTests.ProcessorBackgroundService_WhenMissingDispatcher_ShouldThrowOnBuild`

- **Use case**: When processor is enabled without dispatcher, module build fails
- **Test kind**: Unit
- **Description**: Registration guard
- **Behavior**: `EnableOutboxProcessor` without dispatch adapter
- **Expected outcome**: Build exception
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_enabled_should_throw_before_publish`

- **Use case**: When payload validation is enabled, invalid payload fails before broker publish
- **Test kind**: Unit
- **Description**: `ValidatePayloadBeforeDispatch` gate
- **Behavior**: Dispatch with invalid serialized payload
- **Expected outcome**: Exception before publish; no broker message
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `TransportOutboxDispatcherTests.DispatchAsync_when_validate_payload_disabled_should_publish_without_deserializing`

- **Use case**: When payload validation is disabled, publish proceeds without pre-dispatch deserialize
- **Test kind**: Unit
- **Description**: Fast path without validation
- **Behavior**: Dispatch with validation disabled
- **Expected outcome**: Broker publish without deserialize step
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `InMemoryOutboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry`

- **Use case**: When terminal persist is skipped after successful publish, retry republishes the message
- **Test kind**: Integration
- **Description**: At-least-once publication window
- **Behavior**: Simulated persist skip after broker ack
- **Expected outcome**: Second publish on retry
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`)

#### `OutboxProcessorCorrectnessTests.PipelinedProcessor_when_hook_failure_policy_is_complete_despite_hook_failure_should_mark_published`

- **Use case**: When hook failure policy is complete despite hook failure, publication succeeds despite hook error
- **Test kind**: Unit
- **Description**: Transport default hook policy
- **Behavior**: Post-dispatch hook throws with complete-despite-failure policy
- **Expected outcome**: Published status despite hook error
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `PostgreSqlOutboxEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughPostgreSqlStore`

- **Use case**: When outbox runs against PostgreSQL, events publish to completion
- **Test kind**: Integration
- **Description**: Real PostgreSQL store E2E
- **Behavior**: Enqueue then processor pass
- **Expected outcome**: Published row in database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxProcessorEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore`

- **Use case**: When outbox runs against EF Core, events publish and handlers receive them
- **Test kind**: Integration
- **Description**: EF Core store E2E
- **Behavior**: Full enqueue and process cycle
- **Expected outcome**: Handler receives event; published row
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `AmqpOutboxDispatchIntegrationTests.ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue`

- **Use case**: When AMQP dispatch is configured, processor publishes envelope to the broker queue
- **Test kind**: Integration
- **Description**: AMQP outbox dispatch
- **Behavior**: Process pending through AMQP dispatcher
- **Expected outcome**: Message arrives on AMQP queue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`)

#### `InProcessOutboxDispatcherTests.ProcessPendingAsync_ShouldPublishThroughInProcessOutboxDispatcherAndMarkPublished`

- **Use case**: When in-process dispatch is configured, the event mediator invokes handlers and marks published
- **Test kind**: Unit
- **Description**: In-process outbox dispatcher
- **Behavior**: `ProcessPendingAsync` with in-process dispatcher
- **Expected outcome**: Event handler invoked; published status
- **Remarks**: `LiteBus.Outbox.UnitTests` (`Dispatch/`)

#### `OutboxProcessorLoopErrorTelemetryTests.ExecuteAsync_when_processor_throws_should_increment_loop_errors_counter`

- **Use case**: When the processor throws unexpectedly in the background loop, loop error counter increments
- **Test kind**: Unit
- **Description**: Pass-level exception in hosted loop
- **Behavior**: Simulated processor throw
- **Expected outcome**: `litebus.outbox.processor.loop_errors` incremented
- **Remarks**: `LiteBus.Outbox.UnitTests`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Two-phase broker ack before terminal persist | No | Not shipped |: |: |
| Cross-broker fan-out from one envelope | Yes | Single dispatcher only; fan-out is app logic |: |: |
| Synchronous enqueue-and-publish API | No | Separate writer and processor |: |: |

### Out-of-Scope Use Cases

- Two-phase broker acknowledgment token persisted before terminal state
- Multi-dispatcher load balancing within one module
- Guaranteed exactly-once broker delivery
