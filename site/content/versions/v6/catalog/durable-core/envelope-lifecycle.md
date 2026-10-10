# Envelope Lifecycle

**ID:** `durable-core.envelope-lifecycle`
**Maturity:** GA

## Summary

Inbox and outbox rows move through explicit status values from pending storage through leased processing to terminal completed/published, failed, or dead-lettered states.

## What It Does

Writers create `Pending` envelopes. Processors transition rows to `Processing`/`Publishing` on lease, then persist terminal outcomes. Failed rows become eligible for retry with updated visibility. Exceeded retry policy moves rows to `DeadLettered`. Operators requeue dead letters back to pending. Retention services delete aged terminal rows.

## Public Surface

### Consumer Contracts

- **`InboxEnvelope` / `OutboxEnvelope`**: Persistence and processor row models with status, lease fields, attempt count, and terminal timestamps.
- **`InboxStatus` / `OutboxStatus`**: Lifecycle enums (`Pending`, `Processing`/`Publishing`, `Completed`/`Published`, `Failed`, `DeadLettered`).
- **`IInboxStateWriter` / `IOutboxStateWriter`**: Terminal persist after dispatch (`PersistAsync` with lease-owner conditional semantics).
- **`IInboxManager` / `IOutboxManager`**: Operator replay, status counts, and dead-letter inspection.
- **`IInboxRetentionStore` / `IOutboxRetentionStore`**: Purge aged terminal rows by cutoff timestamp.

### Invocation

- **Processor paths**: Lease transitions row to active processing; terminal persist writes `Completed`/`Published`, `Failed` (with `visible_after`), or `DeadLettered`.
- **`RequeueAsync` / `RequeueDeadLettersAsync`**: Move dead-lettered rows back to `Pending` for operator replay.
- **`GetStatusCountsAsync`**: Diagnostics query returning counts grouped by status.
- **`DeleteCompletedOlderThanAsync` / `DeletePublishedOlderThanAsync`**: Retention purge by age cutoff.

### Registration

- Storage adapters implement lease, state writer, diagnostics, and retention store roles (see durable-storage capability).
- **`EnableInboxProcessor`** / **`EnableOutboxProcessor`** register background services that drive automatic lifecycle transitions beyond `Pending`.
- Retention background services register through module manifest when host options configure cleanup intervals.

### Configuration

- **`InboxProcessorOptions` / `OutboxProcessorOptions`**: Batch size, lease duration, retry policy, and hook failure policy affect transition timing and terminal outcomes.
- **Retention host options**: Cutoff age and poll interval on cleanup background services.
- Store-specific: conditional persist matches `lease_owner` on PostgreSQL and EF adapters.

### Extension Points

- Custom storage must honor lease claim, conditional terminal persist, stale lease reclaim, and status query semantics (store contract tests).
- Processor hooks can influence dead-letter vs retry decisions (see processor-hooks capability).
- Operator tooling uses manager APIs; applications do not mutate status columns directly.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `InboxEnvelope`, `InboxStatus` |
| `LiteBus.Outbox.Abstractions` | `OutboxEnvelope`, `OutboxStatus` |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor state transitions |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Persisted columns and queries |

## Requires

- Storage implementation honoring lease and conditional persist semantics
- Processor enabled for automatic transitions beyond pending
- Retention host options when automated cleanup is desired

## Invariants

| Status | Inbox meaning | Outbox meaning |
| --- | --- | --- |
| Pending | Awaiting lease | Awaiting lease |
| Processing / Publishing | Leased by worker | Leased by worker |
| Completed / Published | Handler/dispatch succeeded | Published successfully |
| Failed | Retry scheduled | Retry scheduled |
| DeadLettered | Retry exhausted or hook policy | Retry exhausted or hook policy |

- Stale `Processing` rows become re-leasable after lease expiry
- Terminal persist matches `lease_owner` on PostgreSQL and EF stores
- Retention cutoff uses `COALESCE(completed_at, created_at)` (inbox) and `COALESCE(published_at, created_at)` (outbox)

## Non-Goals

- Exactly-once terminal state across all failure modes
- Automatic contract upgrade of in-flight payloads
- Cross-axis unified envelope table

## Observability

Lifecycle depth and processor state export through meter `LiteBus.Inbox` and `LiteBus.Outbox`. Register with `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()` from the matching `*.Extensions.OpenTelemetry` packages.

### `litebus.inbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic scrape (10-second cache) via `IInboxDiagnosticsStore.GetStatusCountsAsync`
- **Tags**: `litebus.inbox.status` (`LiteBusInboxTelemetry.QueueStatusAttributeName`) with values `Pending`, `Processing`, `Completed`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Rising `Pending` with flat `Processing` indicates processor lag; elevated `Processing` beyond lease duration suggests stuck leases

### `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic scrape via `IOutboxDiagnosticsStore.GetStatusCountsAsync`
- **Tags**: `litebus.outbox.status` with values `Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Same lag interpretation as inbox; `Publishing` rows past lease expiry are reclaim candidates

### `IInboxManager.GetStatusCountsAsync` / `IOutboxManager.GetStatusCountsAsync`

- **Kind**: Management API (not OpenTelemetry)
- **When used**: Operator dashboards, runbooks, and health probes querying live counts
- **Operational note**: Authoritative for point-in-time inspection; gauges cache for export efficiency

### Retention Cleanup

- **Kind**: Counter `litebus.inbox.cleanup.errors` / `litebus.outbox.cleanup.errors` on purge failure
- **When emitted**: `InboxCleanupBackgroundService` / `OutboxCleanupBackgroundService` catch store errors during delete
- **Operational note**: Alert when cleanup errors persist; terminal rows accumulate without purge

## Deep Docs

- [Architecture](../../architecture/README.md) (store roles, retention)
- [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md)
- [Operations and management](../../operations/README.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`
- `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

### Covered Use Cases

#### `InboxStoreContractTests.LeasePendingAsync_ShouldLeaseAndCompleteCommand`

- **Use case**: When an inbox row is leased and completed, it traverses pending through processing to completed
- **Test kind**: Contract
- **Description**: Store contract suite happy-path lease cycle
- **Behavior**: `LeasePendingAsync` then terminal persist to completed
- **Expected outcome**: Terminal completed status
- **Remarks**: Shared across inbox storage adapters

#### `OutboxStoreContractTests.LeasePendingAsync_ShouldRespectRetryVisibility`

- **Use case**: When an outbox row is leased and published, it traverses pending through publishing to published
- **Test kind**: Contract
- **Description**: Outbox lease and publish cycle
- **Behavior**: Lease then dispatch success persist
- **Expected outcome**: Published terminal state
- **Remarks**: Shared across outbox storage adapters

#### `InboxStoreContractTests.MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter`

- **Use case**: When inbox dispatch fails, the row becomes failed with future retry visibility
- **Test kind**: Contract
- **Description**: Processor failure persist path
- **Behavior**: `MarkFailedAsync` with retry metadata
- **Expected outcome**: Failed status and `visible_after` set
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter`

- **Use case**: When outbox dispatch fails, the row becomes failed with backoff visibility
- **Test kind**: Contract
- **Description**: Dispatch failure persist
- **Behavior**: Failed terminal persist with retry delay
- **Expected outcome**: Failed status and future `visible_after`
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus`

- **Use case**: When inbox retry policy is exhausted, the row moves to dead letter
- **Test kind**: Contract
- **Description**: Max attempts exceeded transition
- **Behavior**: `MoveToDeadLetterAsync`
- **Expected outcome**: DeadLettered status
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus`

- **Use case**: When outbox retry policy is exhausted, the row moves to dead letter
- **Test kind**: Contract
- **Description**: Max attempts exceeded on outbox row
- **Behavior**: Dead letter persist role
- **Expected outcome**: DeadLettered status
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnEnvelopeToPending`

- **Use case**: When an operator requeues a dead-lettered inbox row, it returns to pending
- **Test kind**: Contract
- **Description**: Operator replay at store level
- **Behavior**: `RequeueDeadLetterAsync`
- **Expected outcome**: Pending status restored
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnMessageToPending`

- **Use case**: When an operator requeues a dead-lettered outbox row, it returns to pending
- **Test kind**: Contract
- **Description**: Outbox operator requeue
- **Behavior**: Dead letter to pending transition
- **Expected outcome**: Pending status restored
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand`

- **Use case**: When an inbox lease expires while processing, the row becomes leasable again
- **Test kind**: Contract
- **Description**: Stale processing reclaim
- **Behavior**: Lease expiry then second `LeasePendingAsync`
- **Expected outcome**: Row re-leasable; not stuck in processing
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.LeasePendingAsync_WhenLeaseExpires_ShouldReclaimPublishingMessage`

- **Use case**: When an outbox lease expires while publishing, the row becomes leasable again
- **Test kind**: Contract
- **Description**: Stale publishing reclaim
- **Behavior**: Expired lease on publishing row
- **Expected outcome**: Row re-leasable
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: When diagnostics query runs, counts group correctly by lifecycle status
- **Test kind**: Contract
- **Description**: Mixed-status store population
- **Behavior**: `GetStatusCountsAsync`
- **Expected outcome**: Counts per status match stored rows
- **Remarks**: Store contract suite

#### `InboxRetentionStoreContractTests.DeleteCompletedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: When retention cutoff passes, old completed inbox rows are deleted
- **Test kind**: Contract
- **Description**: Retention purge by age
- **Behavior**: `DeleteCompletedOlderThanAsync`
- **Expected outcome**: Eligible completed rows removed
- **Remarks**: Uses `completed_at` in cutoff semantics

#### `InboxRetentionStoreContractTests.DeleteCompletedOlderThanAsync_WhenCompletedAtIsRecentButCreatedAtIsOld_ShouldRetainRow`

- **Use case**: When `completed_at` is recent but `created_at` is old, retention retains the row
- **Test kind**: Contract
- **Description**: COALESCE cutoff semantics for inbox retention
- **Behavior**: Retention delete with mixed timestamps
- **Expected outcome**: Row kept because `completed_at` drives cutoff
- **Remarks**: Store contract suite

#### `OutboxRetentionStoreContractTests.DeletePublishedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: When retention cutoff passes, old published outbox rows are deleted
- **Test kind**: Contract
- **Description**: Outbox retention purge
- **Behavior**: `DeletePublishedOlderThanAsync`
- **Expected outcome**: Old published rows removed
- **Remarks**: Uses `COALESCE(published_at, created_at)`

#### `InboxCleanupBackgroundServiceTests.ExecuteAsync_ShouldDeleteCompletedRowsOlderThanRetention`

- **Use case**: When the hosted retention loop runs, it invokes purge for aged completed rows
- **Test kind**: Unit
- **Description**: Cleanup background service with configured retention window
- **Behavior**: `ExecuteAsync` retention pass
- **Expected outcome**: Purge store method invoked for eligible rows
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter`

- **Use case**: When a handler fails in a real PostgreSQL deployment, the row is marked failed with retry visibility
- **Test kind**: Integration
- **Description**: Processor failure path end-to-end
- **Behavior**: `ProcessPendingAsync` with throwing handler
- **Expected outcome**: Failed status and `visible_after` in database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter`

- **Use case**: When inbox retry attempts are exhausted in PostgreSQL, the row dead-letters
- **Test kind**: Integration
- **Description**: Retry exhaustion E2E
- **Behavior**: Repeated failures until max attempts
- **Expected outcome**: DeadLettered status in database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlOutboxEndToEndTests.ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter`

- **Use case**: When outbox retry attempts are exhausted in PostgreSQL, the row dead-letters
- **Test kind**: Integration
- **Description**: Outbox retry exhaustion E2E
- **Behavior**: Repeated dispatch failures
- **Expected outcome**: DeadLettered status in database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Automatic contract upgrade of in-flight payloads | No | Not shipped |: |: |
| Cross-axis unified envelope table | No | Separate inbox/outbox tables |: |: |
| Operator purge of in-progress leased rows | No (by design) | Management skips active leases |: |: |

### Out-of-Scope Use Cases

- Exactly-once terminal state across all failure modes
- Unified inbox/outbox lifecycle enum
- Automatic archival to cold storage
