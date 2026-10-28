# Envelope Lifecycle

**ID:** `durable-core.envelope-lifecycle`
**Maturity:** GA

## Summary

Inbox and outbox rows move through explicit status values. They start in pending storage, move through leased processing, and end in a terminal state: completed or published, failed, or dead-lettered.

## What It Does

Writers create `Pending` envelopes. Processors move rows to `Processing` or `Publishing` on lease, then persist the terminal outcomes. Failed rows become eligible for retry with updated visibility. When the retry policy is exceeded, rows move to `DeadLettered`. Operators requeue dead letters back to pending. Retention services delete aged terminal rows.

## Public Surface

### Consumer Contracts

- **`InboxEnvelope`** and **`OutboxEnvelope`**: The persistence and processor row models, with status, lease fields, attempt counts, and terminal timestamps.
- **`InboxStatus`** and **`OutboxStatus`**: The lifecycle enums (`Pending`, `Processing` or `Publishing`, `Completed` or `Published`, `Failed`, `DeadLettered`).
- **`IInboxStateWriter`** and **`IOutboxStateWriter`**: They persist the terminal state after dispatch (`PersistAsync` with lease-owner conditional semantics).
- **`IInboxManager`** and **`IOutboxManager`**: They provide operator replay, status counts, and dead-letter inspection.
- **`IInboxRetentionStore`** and **`IOutboxRetentionStore`**: They purge aged terminal rows by cutoff timestamp.

### Invocation

- **Processor paths**: A lease moves the row to active processing. The terminal persist then writes `Completed` or `Published`, `Failed` (with `visible_after`), or `DeadLettered`.
- **`RequeueAsync`** and **`RequeueDeadLettersAsync`**: They move dead-lettered rows back to `Pending` for operator replay.
- **`GetStatusCountsAsync`**: This diagnostics query returns counts grouped by status.
- **`DeleteCompletedOlderThanAsync`** and **`DeletePublishedOlderThanAsync`**: They run the retention purge by age cutoff.

### Operational Fields

Every envelope carries the fields an operator asks for first, and every store adapter persists and returns them.

| Field | Written when | Answers |
| --- | --- | --- |
| `LastAttemptedAt` | Each lease | When a processor last leased it |
| `FirstFailedAt` | The first failure only | How long the message kept failing before it succeeded or was dead-lettered |
| `DeadLetteredAt` | Dead-letter transition | When the message was dead-lettered |
| `LastLeaseOwner` | Each lease | Which instance handled it. The value survives the lease being released |
| `ErrorType` | Each failure carrying an exception | What kind of failure it was, without parsing `LastError` |
| `Handler` (outbox only) | Enqueue under per-handler delivery | Which single event handler the envelope is addressed to. A `null` value means broadcast |

`Handler` holds the full name of the single event handler a per-handler envelope is addressed to. In-process dispatch publishes a targeted envelope with a handler predicate matching only the recorded handler. See [Per-Handler Delivery](outbox-enqueue.md#per-handler-delivery).

`FirstFailedAt` is set once and is not moved by later failures, so it still identifies the first failure. `AsRequeued` clears `FirstFailedAt`, `DeadLetteredAt`, `ErrorType` and `LastError` because the message starts again from pending, and it keeps `LastAttemptedAt` and `LastLeaseOwner` as the record of what happened before.

Processors build these from a `MessageFailure`, which carries the formatted error, the exception type, and the moment the attempt failed:

```csharp
envelope.AsFailed(MessageFailure.From(exception, clock.GetUtcNow()), visibleAfter);
envelope.AsDeadLettered(MessageFailure.From(exception, clock.GetUtcNow()));
```

`MessageFailure.FromError(text, occurredAt)` covers a failure with a reason but no exception, such as a lost lease. The resulting envelope records no `ErrorType`, which is the correct record when no exception exists.

Because `IInboxManager.QueryAsync` and `IOutboxManager.QueryAsync` return envelopes, a dead-letter listing reads these fields directly from the query result instead of reading store tables.

### Registration

- Storage adapters implement the lease, state writer, diagnostics, and retention store roles (see the durable-storage capability).
- **`EnableInboxProcessor`** and **`EnableOutboxProcessor`** register background services that drive automatic lifecycle transitions beyond `Pending`.
- Retention background services register through the module manifest when the host options configure cleanup intervals.

### Configuration

- **`InboxProcessorOptions`** and **`OutboxProcessorOptions`**: The batch size, lease duration, retry policy, and hook failure policy affect transition timing and terminal outcomes.
- **Retention host options**: They set the cutoff age and poll interval on the cleanup background services.
- Store-specific behavior: the conditional persist matches `lease_owner` on the PostgreSQL and EF adapters.

### Extension Points

- Custom storage must honor lease claim, conditional terminal persist, stale lease reclaim, and status query semantics (store contract tests).
- Processor hooks can influence dead-letter or retry decisions (see the processor-hooks capability).
- Operator tooling uses the manager APIs. Applications do not mutate status columns directly.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `InboxEnvelope`, `InboxStatus` |
| `LiteBus.Outbox.Abstractions` | `OutboxEnvelope`, `OutboxStatus` |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor state transitions |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Persisted columns and queries |

## Requires

- A storage implementation honoring lease and conditional persist semantics
- An enabled processor for automatic transitions beyond pending
- Retention host options when automated cleanup is desired

## Invariants

| Status | Inbox meaning | Outbox meaning |
| --- | --- | --- |
| Pending | Awaiting lease | Awaiting lease |
| Processing / Publishing | Leased by worker | Leased by worker |
| Completed / Published | Handler or dispatch succeeded | Published successfully |
| Failed | Retry scheduled | Retry scheduled |
| DeadLettered | Retry exhausted or hook policy | Retry exhausted or hook policy |

- Stale `Processing` rows become re-leasable after lease expiry
- The terminal persist matches `lease_owner` on the PostgreSQL and EF stores
- The retention cutoff uses `COALESCE(completed_at, created_at)` (inbox) and `COALESCE(published_at, created_at)` (outbox)

## Non-Goals

- Terminal state is not exactly-once across all failure modes
- In-flight payloads do not get automatic contract upgrades
- There is no cross-axis unified envelope table

## Observability

Lifecycle depth and processor state are exported through the `LiteBus.Inbox` and `LiteBus.Outbox` meters. Register them with `AddLiteBusInboxMetrics()` or `AddLiteBusOutboxMetrics()` from the matching `*.Extensions.OpenTelemetry` packages.

### `litebus.inbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: A periodic scrape with a 10-second cache, through `IInboxDiagnosticsStore.GetStatusCountsAsync`
- **Tags**: `litebus.inbox.status` (`LiteBusInboxTelemetry.QueueStatusAttributeName`) with values `Pending`, `Processing`, `Completed`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: A rising `Pending` count with a flat `Processing` count indicates processor lag. An elevated `Processing` count beyond the lease duration suggests stuck leases

### `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: A periodic scrape through `IOutboxDiagnosticsStore.GetStatusCountsAsync`
- **Tags**: `litebus.outbox.status` with values `Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: The lag interpretation is the same as for the inbox. `Publishing` rows past lease expiry are reclaim candidates

### `IInboxManager.GetStatusCountsAsync` / `IOutboxManager.GetStatusCountsAsync`

- **Kind**: Management API (not OpenTelemetry)
- **When used**: Operator dashboards, runbooks, and health probes use it to query live counts
- **Operational note**: It is authoritative for point-in-time inspection. The gauges cache their values for export efficiency

### Retention Cleanup

- **Kind**: Counters `litebus.inbox.cleanup.errors` and `litebus.outbox.cleanup.errors`, incremented on purge failure
- **When emitted**: `InboxCleanupBackgroundService` and `OutboxCleanupBackgroundService` catch store errors during delete
- **Operational note**: Alert when cleanup errors persist. Terminal rows accumulate without a purge

## References

- [Architecture](../../architecture/README.md) (store roles, retention)
- [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md)
- [Operations and management](../../operations/management-endpoints.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Storage.IntegrationTests` (`PostgreSql/`, `EntityFrameworkCore/`)
- `LiteBus.Storage.Testing` (shared inbox/outbox store contract suites)

### Covered Use Cases

#### `InboxStoreContractTests.LeasePendingAsync_ShouldLeaseAndCompleteCommand`

- **Use case**: When an inbox row is leased and completed, it traverses pending through processing to completed
- **Test kind**: Contract
- **Description**: The store contract suite happy-path lease cycle
- **Behavior**: `LeasePendingAsync` runs, then the terminal persist to completed
- **Expected outcome**: Terminal completed status
- **Remarks**: Shared across inbox storage adapters

#### `OutboxStoreContractTests.LeasePendingAsync_ShouldRespectRetryVisibility`

- **Use case**: When an outbox row is leased and published, it traverses pending through publishing to published
- **Test kind**: Contract
- **Description**: The outbox lease and publish cycle
- **Behavior**: The lease runs, then the dispatch success persist
- **Expected outcome**: Published terminal state
- **Remarks**: Shared across outbox storage adapters

#### `InboxStoreContractTests.MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter`

- **Use case**: When inbox dispatch fails, the row becomes failed with future retry visibility
- **Test kind**: Contract
- **Description**: The processor failure persist path
- **Behavior**: `MarkFailedAsync` runs with retry metadata
- **Expected outcome**: Failed status with `visible_after` set
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter`

- **Use case**: When outbox dispatch fails, the row becomes failed with backoff visibility
- **Test kind**: Contract
- **Description**: The dispatch failure persist
- **Behavior**: The failed terminal persist runs with a retry delay
- **Expected outcome**: Failed status with a future `visible_after`
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus`

- **Use case**: When the inbox retry policy is exhausted, the row moves to dead letter
- **Test kind**: Contract
- **Description**: The max attempts exceeded transition
- **Behavior**: `MoveToDeadLetterAsync` runs
- **Expected outcome**: DeadLettered status
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus`

- **Use case**: When the outbox retry policy is exhausted, the row moves to dead letter
- **Test kind**: Contract
- **Description**: Max attempts exceeded on an outbox row
- **Behavior**: The dead letter persist role runs
- **Expected outcome**: DeadLettered status
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnEnvelopeToPending`

- **Use case**: When an operator requeues a dead-lettered inbox row, it returns to pending
- **Test kind**: Contract
- **Description**: Operator replay at store level
- **Behavior**: `RequeueDeadLetterAsync` runs
- **Expected outcome**: Pending status is restored
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnMessageToPending`

- **Use case**: When an operator requeues a dead-lettered outbox row, it returns to pending
- **Test kind**: Contract
- **Description**: An outbox operator requeue
- **Behavior**: The dead letter to pending transition runs
- **Expected outcome**: Pending status is restored
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand`

- **Use case**: When an inbox lease expires while processing, the row becomes leasable again
- **Test kind**: Contract
- **Description**: A stale processing reclaim
- **Behavior**: The lease expires, then a second `LeasePendingAsync` runs
- **Expected outcome**: The row is re-leasable and not stuck in processing
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.LeasePendingAsync_WhenLeaseExpires_ShouldReclaimPublishingMessage`

- **Use case**: When an outbox lease expires while publishing, the row becomes leasable again
- **Test kind**: Contract
- **Description**: A stale publishing reclaim
- **Behavior**: The lease expires on a publishing row
- **Expected outcome**: The row is re-leasable
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: When the diagnostics query runs, counts group correctly by lifecycle status
- **Test kind**: Contract
- **Description**: A mixed-status store population
- **Behavior**: `GetStatusCountsAsync` runs
- **Expected outcome**: Counts per status match the stored rows
- **Remarks**: Store contract suite

#### `InboxRetentionStoreContractTests.DeleteCompletedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: When the retention cutoff passes, old completed inbox rows are deleted
- **Test kind**: Contract
- **Description**: A retention purge by age
- **Behavior**: `DeleteCompletedOlderThanAsync` runs
- **Expected outcome**: Eligible completed rows are removed
- **Remarks**: Uses `completed_at` in cutoff semantics

#### `InboxRetentionStoreContractTests.DeleteCompletedOlderThanAsync_WhenCompletedAtIsRecentButCreatedAtIsOld_ShouldRetainRow`

- **Use case**: When `completed_at` is recent but `created_at` is old, retention retains the row
- **Test kind**: Contract
- **Description**: COALESCE cutoff semantics for inbox retention
- **Behavior**: A retention delete with mixed timestamps
- **Expected outcome**: The row is kept because `completed_at` drives the cutoff
- **Remarks**: Store contract suite

#### `OutboxRetentionStoreContractTests.DeletePublishedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: When the retention cutoff passes, old published outbox rows are deleted
- **Test kind**: Contract
- **Description**: An outbox retention purge
- **Behavior**: `DeletePublishedOlderThanAsync` runs
- **Expected outcome**: Old published rows are removed
- **Remarks**: Uses `COALESCE(published_at, created_at)`

#### `InboxCleanupBackgroundServiceTests.ExecuteAsync_ShouldDeleteCompletedRowsOlderThanRetention`

- **Use case**: When the hosted retention loop runs, it invokes purge for aged completed rows
- **Test kind**: Unit
- **Description**: The cleanup background service with a configured retention window
- **Behavior**: An `ExecuteAsync` retention pass
- **Expected outcome**: The purge store method runs for eligible rows
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter`

- **Use case**: When a handler fails in a real PostgreSQL deployment, the row is marked failed with retry visibility
- **Test kind**: Integration
- **Description**: The processor failure path end to end
- **Behavior**: `ProcessPendingAsync` runs with a throwing handler
- **Expected outcome**: Failed status and `visible_after` in the database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter`

- **Use case**: When inbox retry attempts are exhausted in PostgreSQL, the row dead-letters
- **Test kind**: Integration
- **Description**: Retry exhaustion end to end
- **Behavior**: Repeated failures until max attempts
- **Expected outcome**: DeadLettered status in the database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlOutboxEndToEndTests.ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter`

- **Use case**: When outbox retry attempts are exhausted in PostgreSQL, the row dead-letters
- **Test kind**: Integration
- **Description**: Outbox retry exhaustion end to end
- **Behavior**: Repeated dispatch failures
- **Expected outcome**: DeadLettered status in the database
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Automatic contract upgrade of in-flight payloads | No | Not shipped | N/A | N/A |
| Cross-axis unified envelope table | No | Separate inbox and outbox tables | N/A | N/A |
| Operator purge of in-progress leased rows | No (by design) | Management skips active leases | N/A | N/A |

### Out-of-Scope Use Cases

- Exactly-once terminal state across all failure modes
- A unified inbox and outbox lifecycle enum
- Automatic archival to cold storage
