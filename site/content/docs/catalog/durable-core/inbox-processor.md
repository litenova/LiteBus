# Inbox Processor

**ID:** `durable-core.inbox-processor`
**Maturity:** GA

## Summary

Background worker that leases due inbox envelopes, dispatches them through `IInboxDispatcher`, and records terminal state with configurable concurrency and lease heartbeat.

## What It Does

`PipelinedInboxProcessor` is the sole inbox processor implementation. Each pass leases a batch, fans work to `DispatcherConcurrency` workers with per-message DI scopes, renews leases on heartbeat interval, runs optional envelope hooks, and persists outcomes per envelope. `InboxProcessorBackgroundService` holds one processor instance for stable `LeaseOwner`. Registration requires exactly one dispatcher when the processor is enabled.

## Public Surface

### Consumer Contracts

- **`IInboxProcessor`**: Processor contract (`ProcessPendingAsync`) for manual passes and testing.
- **`PipelinedInboxProcessor`**: Default pipelined implementation with lease heartbeat and concurrent dispatch workers.
- **`IInboxProcessorControl`**: Pause, drain, and resume for operational control; exposed through management endpoints when ASP.NET extensions are installed.
- **`InboxProcessorOptions`**: Batch size, lease duration, heartbeat interval, retry policy, dispatcher concurrency, hook failure policy, and optional tenant filter.
- **`InboxProcessorHostOptions`**: Background loop poll interval and adaptive polling tuning.

### Invocation

- **`ProcessPendingAsync(CancellationToken)`**: One processor pass: lease batch, dispatch each envelope, persist terminal state.
- **`PauseAsync` / `DrainAsync` / `ResumeAsync`**: Operational control on `IInboxProcessorControl`.
- Background service calls `ProcessPendingAsync` on a poll interval until host shutdown.

Typical deployment: enable processor on generic host; application code accepts commands via `IInbox`; processor executes handlers asynchronously.

### Registration

- **`EnableInboxProcessor(host => ...)`** on inbox module builder registers manifest background service.
- **`UseProcessorOptions(InboxProcessorOptions)`** configures batch, lease, retry, and concurrency.
- **`Use*Storage()`** and exactly one **`Use*Dispatch()`** must be present in the same inbox builder; callback order is irrelevant.
- **`IInboxProcessor`** registers transient; background service holds a singleton processor instance for stable lease owner.

### Configuration

- **`InboxProcessorOptions`**: `BatchSize`, `LeaseDuration`, `LeaseHeartbeatInterval` (must be `<= LeaseDuration / 2`), `Retry`, `DispatcherConcurrency`, `HookFailurePolicy` (default `DeadLetter`), optional `TenantId`.
- **`InboxProcessorHostOptions.PollInterval`**: Delay between passes; adaptive polling when full batches return quickly.
- Compose-time validation rejects invalid batch size and max attempts.

### Extension Points

- **`IInboxDispatcher`**: Exactly one dispatcher per inbox module (in-process command mediator or transport adapter).
- **`IInboxProcessorEnvelopeHook`**: Pre/post dispatch hooks with configurable failure policy (see processor-hooks capability).
- Custom storage implements lease and state writer roles required by the pipeline.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox` | Processor, factory, background service |
| `LiteBus.Inbox.Abstractions` | `InboxProcessorOptions`, `IInboxProcessor` |
| `LiteBus.Runtime.Extensions.Hosting` | Generic host bridging |
| `LiteBus.Inbox.Dispatch.*` | Required dispatcher |

## Requires

- `Use*Storage` and exactly one `Use*Dispatch` before `EnableInboxProcessor`
- Registered command handlers for in-process dispatch path
- Store implementing lease and state writer roles

## Invariants

- `IInboxProcessor` registers transient but background service holds singleton instance
- Per-message persist failures log and continue pass; failed envelope keeps lease until expiry
- `LeaseHeartbeatInterval` must be `<= LeaseDuration / 2`
- Default `HookFailurePolicy` is `DeadLetter` for inbox processors
- `TenantId` on options limits leasing to one tenant partition when set

## Non-Goals

- Synchronous accept-and-execute in one call
- Multiple dispatchers per inbox module
- Exactly-once handler execution

## Observability

Inbox processor instrumentation uses meter `LiteBus.Inbox` (`LiteBusInboxTelemetry.MeterName`) and activity source `LiteBus.Inbox`. Register with `AddLiteBusInboxMetrics()` and `AddLiteBusInboxInstrumentation()` from `LiteBus.Inbox.Extensions.OpenTelemetry`.

### `litebus.inbox.processor.state`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Scrape when `IInboxProcessorControl` is registered
- **Value**: `0` Running, `1` Paused, `2` Draining
- **Operational note**: Non-zero state during maintenance windows is expected; stuck Paused indicates forgotten resume

### `litebus.inbox.processor.passes`

- **Kind**: Counter (internal name; stable in code)
- **When emitted**: Once per completed processor pass
- **Operational note**: Flat passes with rising queue depth indicates lease or dispatch bottleneck

### `litebus.inbox.processor.succeeded`

- **Kind**: Counter
- **When emitted**: Each envelope marked completed in a pass
- **Operational note**: Compare to `leases_acquired` for pass efficiency

### `litebus.inbox.processor.failed`

- **Kind**: Counter
- **When emitted**: Each envelope marked failed (retry scheduled) in a pass
- **Operational note**: Sustained rate may indicate handler exceptions or dispatch errors

### `litebus.inbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: Each envelope moved to dead letter in a pass
- **Operational note**: Alert when rate exceeds operator tolerance; inspect dead-letter queue via manager APIs

### `litebus.inbox.processor.loop_errors`

- **Kind**: Counter
- **When emitted**: Background loop catches unhandled exception outside per-envelope handling
- **Operational note**: Indicates infrastructure or store failures; investigate logs immediately

### `litebus.inbox.processor.leases_acquired`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorLeasesAcquiredInstrumentName`)
- **When emitted**: Sum of envelopes leased at start of pass
- **Operational note**: Zero leases with pending depth suggests visibility or tenant filter mismatch

### `litebus.inbox.processor.lease_lost`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorLeaseLostInstrumentName`)
- **When emitted**: Lease renewal fails during dispatch; dispatch is canceled
- **Operational note**: Correlates with long handlers or store renewal failures

### `litebus.inbox.processor.persist_skipped` / `litebus.inbox.processor.persist_rejected`

- **Kind**: Counter (both increment on lease-lost terminal persist skip)
- **When emitted**: Terminal persist skipped because active lease no longer matches
- **Operational note**: Expected under concurrent reclaim; row retries on next visibility

### `litebus.inbox.processor.persist_failed`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorPersistFailedInstrumentName`)
- **When emitted**: Terminal persist throws after successful handler dispatch (exception is logged; pass continues)
- **Operational note**: Row remains leased until expiry and may redispatch; pair with `litebus.inbox.processor.succeeded` and handler idempotency

### `litebus.inbox.diagnostics.unavailable`

- **Kind**: Counter (`LiteBusInboxTelemetry.DiagnosticsUnavailableInstrumentName`)
- **When emitted**: Queue depth diagnostic probe fails against the backing store
- **Operational note**: Observable `litebus.inbox.queue.depth` may be stale while this counter rises; investigate store connectivity

### `litebus.inbox.processor.dispatch_duration`

- **Kind**: Histogram in milliseconds (`LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName`)
- **When emitted**: After each envelope dispatch completes
- **Operational note**: Tail latency drives lease duration and heartbeat tuning

### Management Endpoints

- **Kind**: HTTP control (ASP.NET extensions)
- **Routes**: `POST /litebus/inbox/processor/pause`, `POST /litebus/inbox/processor/drain`, and `POST /litebus/inbox/processor/resume`
- **Operational note**: Route tests verify state transitions and forward drain timeouts to the processor control service

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (processing flow, processor lifetime)
- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md) (pipelined processor)

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
- **Expected outcome**: Handler executes once; row completes
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
- **Behavior**: `PauseAsync` then pass attempt; `ResumeAsync`
- **Expected outcome**: No dispatches while paused; processing resumes after resume
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorControlTests.DrainAsync_should_process_pending_messages_and_complete`

- **Use case**: When drain is signaled, in-flight work completes and pending rows are cleared
- **Test kind**: Unit
- **Description**: Graceful drain API
- **Behavior**: `DrainAsync` with pending rows
- **Expected outcome**: Pending cleared; processor reaches drained state
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
- **Behavior**: Multiple commands accepted; short poll window
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
- **Expected outcome**: Dispatch canceled; retryable failed outcome
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
- **Behavior**: Insert row; observe processor wake
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
- **Description**: Two pending rows use `tenant-a` and `tenant-b`; each processor configures one tenant filter
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
| Processor disabled mid-pass cancellation | Yes | Cancellation tested; not host SIGTERM | Integration | Low |

### Out-of-Scope Use Cases

- Synchronous accept-and-execute in one API call
- Multiple dispatchers per inbox module
- Exactly-once handler execution
