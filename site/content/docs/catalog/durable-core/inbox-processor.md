# Inbox Processor

**ID:** `durable-core.inbox-processor`
**Maturity:** GA

## Summary

The inbox processor is a background worker that leases due inbox envelopes, dispatches them through `IInboxDispatcher`, and records their terminal state. It supports configurable concurrency, and a lease heartbeat keeps long dispatches alive.

## What It Does

`PipelinedInboxProcessor` is the only inbox processor implementation. Each pass leases a batch of envelopes, spreads the work across `DispatcherConcurrency` workers with one DI scope per message, renews leases on the heartbeat interval, runs the optional envelope hooks, and persists one outcome per envelope. `InboxProcessorBackgroundService` holds one processor instance so the `LeaseOwner` stays stable. When the processor is enabled, the module needs exactly one dispatcher.

## Public Surface

### Consumer Contracts

- **`IInboxProcessor`**: The processor contract. Use `ProcessPendingAsync` for manual passes and testing.
- **`PipelinedInboxProcessor`**: The default pipelined implementation. It renews leases on a heartbeat and dispatches through concurrent workers.
- **`IInboxProcessorControl`**: Offers pause, drain, and resume for operational control. Management endpoints expose it when the ASP.NET extensions are installed.
- **`InboxProcessorOptions`**: Carries the batch size, lease duration, heartbeat interval, retry policy, dispatcher concurrency, hook failure policy, and an optional tenant filter.
- **`InboxProcessorHostOptions`**: Carries the background loop poll interval and adaptive polling tuning.

### Invocation

- **`ProcessPendingAsync(CancellationToken)`**: Runs one processor pass: it leases a batch, dispatches each envelope, and persists the terminal state.
- **`PauseAsync` / `DrainAsync` / `ResumeAsync`**: Pause, drain, and resume the processor through `IInboxProcessorControl`.
- The background service calls `ProcessPendingAsync` on a poll interval until host shutdown.

In a typical deployment, the processor runs on the generic host, application code accepts commands through `IInbox`, and the processor executes handlers asynchronously.

### Registration

- Call **`EnableInboxProcessor(host => ...)`** on the inbox module builder to register the manifest background service.
- Call **`UseProcessorOptions(InboxProcessorOptions)`** to configure batch, lease, retry, and concurrency.
- Register **`Use*Storage()`** and exactly one **`Use*Dispatch()`** in the same inbox builder, in any order.
- The background service holds a singleton processor instance so the lease owner stays stable, even though **`IInboxProcessor`** is registered as transient.

### Configuration

- **`InboxProcessorOptions`**: `BatchSize` (default 20), `LeaseDuration` (default one minute), `LeaseHeartbeatInterval` (default 15 seconds, which must be `<= LeaseDuration / 2`), `Retry`, `DispatcherConcurrency` (default 1), `HookFailurePolicy` (`null` by default, which the inbox builder resolves to `DeadLetter`), and optional `TenantId` (when `null`, the processor leases across tenants).
- **`InboxProcessorHostOptions.PollInterval`**: The delay between passes. Polling adapts when full batches return quickly.

Set `Retry.IsPermanentFailure` to dead-letter application failures that can never succeed on retry. See [Permanent Failures](lease-retry-dead-letter.md#permanent-failures).
- Compose-time validation rejects an invalid batch size and invalid max attempts.

### Extension Points

- **`IInboxDispatcher`**: Each inbox module takes exactly one dispatcher: the in-process command mediator or a transport adapter.
- **`IProcessorEnvelopeHook`** (`LiteBus.Messaging.Abstractions.Processing`): Runs before and after dispatch, with a configurable failure policy (see processor-hooks capability).
- Custom storage implements the lease and state writer roles the pipeline requires.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox` | Processor, factory, background service |
| `LiteBus.Inbox.Abstractions` | `InboxProcessorOptions`, `IInboxProcessor` |
| `LiteBus.Runtime.Extensions.Hosting` | Generic host bridging |
| `LiteBus.Inbox.Dispatch.*` | Required dispatcher |

## Requires

- Register `Use*Storage` and exactly one `Use*Dispatch` before `EnableInboxProcessor`
- Register command handlers for the in-process dispatch path
- Use a store that implements the lease and state writer roles

## Invariants

- The background service holds a singleton processor instance, even though `IInboxProcessor` is registered as transient
- Completion timestamps use the processor clock, so they follow a configured or fake clock instead of wall-clock time
- When a per-message persist fails, the processor logs the failure and continues the pass. The failed envelope keeps its lease until expiry
- `LeaseHeartbeatInterval` must be `<= LeaseDuration / 2`
- By default `HookFailurePolicy` is `null`, which means the dispatcher recommendation. The inbox builder always resolves an unset policy to `DeadLetter`, and an explicit value takes precedence
- When set, `TenantId` on the options limits leasing to one tenant partition

## Non-Goals

- Synchronous accept-and-execute in one call
- Multiple dispatchers per inbox module
- Exactly-once handler execution

## Observability

Inbox processor instrumentation uses meter `LiteBus.Inbox` (`LiteBusInboxTelemetry.MeterName`) and activity source `LiteBus.Inbox`. Register with `AddLiteBusInboxMetrics()` and `AddLiteBusInboxInstrumentation()` from `LiteBus.Inbox.Extensions.OpenTelemetry`. The [OpenTelemetry Metric Catalog](../../architecture/README.md#opentelemetry-metric-catalog) lists every public instrument name with its meter, constant, and description. The table below adds what each instrument means to an operator.

| Instrument | Operator Meaning |
|---|---|
| `litebus.inbox.processor.state` | Recorded on scrape when `IInboxProcessorControl` is registered. A non-zero state is expected during maintenance windows. A state stuck at Paused means the operator never resumed the processor. |
| `litebus.inbox.processor.passes` | Recorded once per completed processor pass. A flat pass count with rising queue depth indicates a lease or dispatch bottleneck. |
| `litebus.inbox.processor.succeeded` | Recorded for each envelope marked completed in a pass. Compare with `leases_acquired` for pass efficiency. |
| `litebus.inbox.processor.failed` | Recorded for each envelope marked failed (retry scheduled) in a pass. A sustained rate may indicate handler exceptions or dispatch errors. |
| `litebus.inbox.processor.dead_lettered` | Recorded for each envelope moved to dead letter in a pass. Alert when the rate exceeds operator tolerance, and inspect the dead-letter queue through the manager APIs. |
| `litebus.inbox.processor.loop_errors` | Recorded when the background loop catches an unhandled exception outside per-envelope handling. It indicates infrastructure or store failures, so investigate the logs immediately. |
| `litebus.inbox.processor.leases_acquired` | Recorded as the sum of envelopes leased at the start of a pass. Zero leases alongside pending depth suggests a visibility or tenant filter mismatch. |
| `litebus.inbox.processor.lease_lost` | Recorded when lease renewal fails during dispatch and dispatch is canceled. It correlates with long handlers or store renewal failures. |
| `litebus.inbox.processor.persist_skipped`, `litebus.inbox.processor.persist_rejected` | Recorded when terminal persist is skipped because the active lease no longer matches. This is expected under concurrent reclaim. The row becomes visible again, so a later pass retries it. |
| `litebus.inbox.processor.persist_failed` | Recorded when terminal persist throws after successful handler dispatch (the processor logs the exception and continues the pass). The row stays leased until expiry and may be dispatched again. Read it together with `litebus.inbox.processor.succeeded` and handler idempotency. |
| `litebus.inbox.diagnostics.unavailable` | Recorded when the queue depth diagnostic probe fails against the backing store. Observable `litebus.inbox.queue.depth` may be stale while this counter rises. Investigate store connectivity. |
| `litebus.inbox.processor.dispatch_duration` | Recorded after each envelope dispatch completes. Tail latency drives lease duration and heartbeat tuning. |

### Management Endpoints

- **Kind**: HTTP control (ASP.NET extensions)
- **Routes**: `POST /litebus/inbox/processor/pause`, `POST /litebus/inbox/processor/drain`, and `POST /litebus/inbox/processor/resume`
- **Operational note**: The routes forward drain timeouts to the processor control service, and the route tests verify the state transitions

## References

- [Inbox](../../reliable-messaging/inbox.md) (processing flow and processor lifetime)
- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md) (the pipelined processor)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

### Covered Use Cases

#### `PipelinedInboxProcessorTests.PipelinedProcessor_WithConcurrencyOne_ShouldProcessAllCommands`

- **Use case**: When concurrency is one, a single worker processes all pending commands in the pass
- **Test kind**: Unit
- **Description**: In-memory processor with batch lease
- **Behavior**: `ProcessPendingAsync` with `DispatcherConcurrency = 1`
- **Expected outcome**: All rows reach completed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `PipelinedInboxProcessorTests.PipelinedProcessor_WithHeartbeat_ShouldCompleteSlowHandlerWithoutReclaim`

- **Use case**: When heartbeat renews the lease, a slow handler completes without duplicate dispatch
- **Test kind**: Unit
- **Description**: Handler duration exceeds default lease without heartbeat failure
- **Behavior**: Lease renewal during dispatch
- **Expected outcome**: The handler executes once, and the row completes
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `PipelinedInboxProcessorTests.PipelinedProcessor_WithParallelWorkers_ShouldDispatchConcurrently`

- **Use case**: When dispatcher concurrency is greater than one, handlers run in parallel
- **Test kind**: Unit
- **Description**: Multiple pending commands and concurrency > 1
- **Behavior**: Parallel dispatch workers in one pass
- **Expected outcome**: Concurrent handler invocations observed
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorControlTests.PauseAsync_should_block_processing_until_resume`

- **Use case**: When the processor is paused, no new dispatches occur until resume
- **Test kind**: Unit
- **Description**: Processor control API
- **Behavior**: `PauseAsync`, then a pass attempt, then `ResumeAsync`
- **Expected outcome**: No dispatches while paused, and processing resumes after resume
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorControlTests.DrainAsync_should_process_pending_messages_and_complete`

- **Use case**: When drain is signaled, in-flight work completes and pending rows are cleared
- **Test kind**: Unit
- **Description**: Graceful drain API
- **Behavior**: `DrainAsync` with pending rows
- **Expected outcome**: The pending rows clear and the processor reaches the drained state
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxHostingTests.ProcessorBackgroundService_ShouldProcessScheduledCommands`

- **Use case**: When the hosted background loop runs, scheduled commands are processed to completion
- **Test kind**: Unit
- **Description**: Generic host background service wiring
- **Behavior**: `InboxProcessorBackgroundService.ExecuteAsync`
- **Expected outcome**: Commands reach completed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxHostingTests.ProcessorBackgroundService_WhenDispatcherMissing_ShouldThrowOnBuild`

- **Use case**: When processor is enabled without a dispatcher, module build fails
- **Test kind**: Unit
- **Description**: Registration guard (LB1014-style)
- **Behavior**: `EnableInboxProcessor` without `Use*Dispatch`
- **Expected outcome**: Build-time configuration exception
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxHostingTests.ProcessorBackgroundService_WithAdaptivePollingAndFullBatch_ShouldProcessMultipleCommandsQuickly`

- **Use case**: When adaptive polling is enabled and batches are full, the loop processes work quickly
- **Test kind**: Unit
- **Description**: Poll tuning with full batch returns
- **Behavior**: Multiple commands accepted in a short poll window
- **Expected outcome**: High throughput within test window
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxTests.InboxProcessor_WithInvalidMaxAttempts_ShouldThrow`

- **Use case**: When max attempts is invalid, configuration fails at compose time
- **Test kind**: Unit
- **Description**: Options validation on retry policy
- **Behavior**: `UseProcessorOptions` with invalid max attempts
- **Expected outcome**: Configuration exception
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.InboxProcessor_WithInvalidBatchSize_ShouldThrow`

- **Use case**: When batch size is invalid, configuration fails at compose time
- **Test kind**: Unit
- **Description**: Options validation
- **Behavior**: Invalid `BatchSize` in processor options
- **Expected outcome**: Configuration exception
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_ShouldRespectBatchSize`

- **Use case**: When batch size is configured, each pass leases at most that many rows
- **Test kind**: Unit
- **Description**: More pending rows than batch size
- **Behavior**: Single `ProcessPendingAsync` pass
- **Expected outcome**: At most N rows leased per pass
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_ShouldProcessMultipleCommandsInSinglePass`

- **Use case**: When multiple commands are pending, one pass processes all within batch limit
- **Test kind**: Unit
- **Description**: Multi-row single pass
- **Behavior**: `ProcessPendingAsync` with several pending rows
- **Expected outcome**: All rows processed in one pass
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorCorrectnessTests.ValidateOptions_when_heartbeat_exceeds_half_lease_duration_should_throw`

- **Use case**: When heartbeat interval exceeds half lease duration, validation fails
- **Test kind**: Unit
- **Description**: `LeaseHeartbeatInterval` invariant
- **Behavior**: Options validation at processor construction
- **Expected outcome**: Exception when heartbeat too large
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorCorrectnessTests.PipelinedProcessor_when_lease_renewal_fails_should_cancel_dispatch`

- **Use case**: When lease renewal fails during dispatch, dispatch is canceled and retry is scheduled
- **Test kind**: Unit
- **Description**: Heartbeat failure path
- **Behavior**: Simulated renewal failure during handler execution
- **Expected outcome**: Dispatch canceled, with a retryable failed outcome
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorLoopErrorTelemetryTests` (Processor Throws in Pass)

- **Use case**: When an unexpected exception escapes the pass, loop error counter increments
- **Test kind**: Unit
- **Description**: Pass-level unhandled exception
- **Behavior**: Processor throw outside per-envelope handling
- **Expected outcome**: `litebus.inbox.processor.loop_errors` counter incremented
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorBulkTerminalStateTests.ProcessPendingAsync_when_max_attempts_exceeded_should_persist_each_dead_letter`

- **Use case**: When max attempts is exceeded for multiple envelopes, each dead letter is persisted individually
- **Test kind**: Unit
- **Description**: Batch terminal writes on retry exhaustion
- **Behavior**: Multiple rows exceeding max attempts in one pass
- **Expected outcome**: Each row dead-lettered in store
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorPersistFailedTelemetryTests.PipelinedProcessor_when_persist_throws_should_increment_persist_failed_metric`

- **Use case**: When terminal persist throws after successful dispatch, the persist_failed counter increments
- **Test kind**: Unit
- **Description**: Swallowed persist exception telemetry
- **Behavior**: `MarkCompleted` throws after handler success
- **Expected outcome**: `litebus.inbox.processor.persist_failed` incremented
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InMemoryInboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry`

- **Use case**: When terminal persist is skipped after successful dispatch, retry redispatches the command
- **Test kind**: Integration
- **Description**: At-least-once execution window after dispatch
- **Behavior**: Simulated persist skip after handler success
- **Expected outcome**: Handler invoked twice on lease reclaim
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`)

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore`

- **Use case**: When inbox runs against PostgreSQL, scheduled commands execute to completion
- **Test kind**: Integration
- **Description**: Real store plus hosted processor path
- **Behavior**: Accept then processor pass
- **Expected outcome**: Completed row in PostgreSQL
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreInboxProcessorEndToEndTests.ProcessPendingAsync_ShouldExecuteScheduledCommandThroughEfCoreStore`

- **Use case**: When inbox runs against EF Core storage, scheduled commands execute to completion
- **Test kind**: Integration
- **Description**: EF Core store processor E2E
- **Behavior**: Full accept and process cycle
- **Expected outcome**: Completed row in EF store
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `PostgreSqlInboxHostingIntegrationTests.ProcessorBackgroundService_WhenPendingRowInserted_ShouldWakeViaNotifyBeforePollTimeout`

- **Use case**: When a pending row is inserted, PostgreSQL NOTIFY wakes the processor before poll timeout
- **Test kind**: Integration
- **Description**: Work signal integration
- **Behavior**: Insert a row and observe the processor wake
- **Expected outcome**: Processor runs before poll interval elapses
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlModuleRegistrationTests.EnableInboxProcessor_WithStorageAndDispatcher_ShouldRegisterProcessorBackgroundService`

- **Use case**: When inbox processor is enabled with storage and dispatcher, manifest includes background service
- **Test kind**: Integration
- **Description**: Module registration manifest
- **Behavior**: `EnableInboxProcessor` module build
- **Expected outcome**: Processor background service in host manifest
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `TenantScopedInboxProcessorTests.ProcessPendingAsync_WithTenantFilters_ShouldIsolateProcessorPasses`

- **Use case**: Dedicated processors share one store while leasing separate tenant partitions
- **Test kind**: Unit with the complete processor and store path
- **Description**: Two pending rows use `tenant-a` and `tenant-b`, and each processor configures one tenant filter
- **Behavior**: The tenant A pass runs before the tenant B pass
- **Expected outcome**: Each pass completes only its configured tenant row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `ManagementEndpointOperationsTests.ProcessorRoutes_WithRegisteredControls_ShouldInvokeBothAxes`

- **Use case**: Operators pause, drain, and resume the inbox processor over HTTP
- **Test kind**: ASP.NET TestServer integration
- **Description**: Management routes call a recording `IInboxProcessorControl`
- **Behavior**: State, pause, drain, and resume routes execute with explicit drain timeouts
- **Expected outcome**: HTTP 200 responses and exact control-service arguments
- **Remarks**: `LiteBus.Extensions.UnitTests`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Processor disabled mid-pass cancellation | Yes | Cancellation is tested, but host SIGTERM is not | Integration | Low |

### Out-of-Scope Use Cases

- Synchronous accept-and-execute in one API call
- Multiple dispatchers per inbox module
- Exactly-once handler execution
