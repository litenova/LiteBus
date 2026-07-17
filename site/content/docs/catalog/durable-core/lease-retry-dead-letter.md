# Lease, Retry, and Dead Letter

**ID:** `durable-core.lease-retry-dead-letter`
**Maturity:** GA

## Summary

Processors claim work with time-bounded leases, renew leases during long handlers, retry failures with backoff, and move exhausted messages to dead letter for operator replay.

## What It Does

Lease stores atomically claim due rows with `lease_owner` and `lease_expires_at`. Heartbeat renewal starts as soon as each envelope is leased, including while it waits for a worker slot, and continues through terminal persistence. On failure, processors persist `Failed` with retry visibility computed from `RetryOptions`. When `AttemptCount` reaches `MaxAttempts`, rows move to `DeadLettered`. Operators requeue via `IInboxManager` / `IOutboxManager`. Lease loss during dispatch persists retryable failure instead of leaving row stuck in processing.

## Public Surface

### Consumer Contracts

- **`ProcessorOptions`**: Shared base for inbox/outbox processor options including lease and retry settings.
- **`RetryOptions`**: `MaxAttempts`, initial delay, backoff strategy, multiplier, and jitter.
- **`IInboxLeaseStore` / `IOutboxLeaseStore`**: Atomic claim of due pending or failed-visible rows.
- **`LeaseRenewalRequest`**: Heartbeat input carrying envelope id, lease owner, and extension duration.
- **`IInboxManager` / `IOutboxManager`**: Operator requeue and dead-letter query APIs.
- **`RequeueResult`**: Reports requested vs successfully requeued counts for partial requeue scenarios.

### Invocation

- **`LeasePendingAsync`**: Claim batch with new `lease_owner`, increment attempt count, set `lease_expires_at`.
- **Heartbeat renewal**: Extend `lease_expires_at` while dispatch runs; failure cancels dispatch and schedules retry.
- **Failure persist**: Set `Failed` with `visible_after` from backoff calculation.
- **Dead letter persist**: Transition to `DeadLettered` when attempts exhausted.
- **`RequeueAsync` / `RequeueDeadLettersAsync`**: Operator moves dead-lettered rows to `Pending`.

### Registration

- Storage adapters implement lease store and conditional state writer roles.
- Processors register through **`EnableInboxProcessor`** / **`EnableOutboxProcessor`** with **`UseProcessorOptions`** supplying lease and retry values.
- Managers register with inbox/outbox modules for operator tooling.

### Configuration

- **`LeaseDuration`**: Claim TTL; stale processing rows become reclaimable after expiry.
- **`LeaseHeartbeatInterval`**: Renewal cadence; must be `<= LeaseDuration / 2`.
- **`Retry`**: `MaxAttempts`, delay, fixed or exponential backoff, optional jitter.
- **`LeaseOwner`**: Optional operator label with an opaque random session suffix per processor instance. The suffix fences stale sessions that reuse the same configured label.

### Extension Points

- Custom storage must implement atomic lease, renewal, conditional persist on `lease_owner`, and requeue roles.
- Hook failure policy can force dead letter before max attempts (see processor-hooks capability).
- Ingress poison handling is separate from processor dead letter (see ingress ack-policy capability).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `RetryOptions`, `ProcessorOptions` |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor lease orchestration |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Atomic lease and conditional persist |

## Requires

- `LeaseDuration > 0` for meaningful recovery
- Store support for conditional terminal persist on `lease_owner`
- Operator runbook for dead-letter inspection and requeue

## Invariants

- Another worker may lease the same row after lease expiry
- Terminal persist uses `lease_owner` match to avoid overwriting newer attempts (PostgreSQL, EF)
- `Retry.MaxAttempts` counts dispatch attempts on leased envelope
- Requeue only moves rows from dead-letter state (partial requeue reported in `RequeueResult`)
- Heartbeat failure during dispatch to retryable failed outcome, not indefinite processing
- Queued leased envelopes renew their leases before a worker slot becomes available.

## Non-Goals

- Broker-native DLQ replacement for inbox ingress (ingress has separate poison policy)
- Infinite retry without dead letter
- Global ordering guarantees across workers

## Observability

Lease and retry outcomes surface through processor counters on meters `LiteBus.Inbox` and `LiteBus.Outbox`. Register with `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`.

### `litebus.inbox.processor.failed` / `litebus.outbox.processor.failed`

- **Kind**: Counter
- **When emitted**: Each envelope marked failed with retry visibility after dispatch or lease-loss failure
- **Operational note**: Track retry rate vs succeeded/published; spikes indicate handler or broker instability

### `litebus.inbox.processor.dead_lettered` / `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: Retry exhaustion or hook policy moves row to dead letter
- **Operational note**: Primary alert for operator intervention; pair with manager requeue workflows

### `litebus.inbox.processor.lease_lost` / `litebus.outbox.processor.lease_lost`

- **Kind**: Counter (`ProcessorLeaseLostInstrumentName`)
- **When emitted**: Heartbeat renewal fails; dispatch canceled
- **Operational note**: Tune lease duration and heartbeat when this correlates with slow handlers

### `litebus.inbox.processor.persist_skipped` / `litebus.outbox.processor.persist_skipped`

- **Kind**: Counter
- **When emitted**: Terminal persist skipped because lease was reclaimed by another worker
- **Operational note**: Expected under concurrent workers; row retries rather than staying stuck

### `litebus.inbox.processor.persist_rejected` / `litebus.outbox.processor.persist_rejected`

- **Kind**: Counter
- **When emitted**: Conditional persist rejects update due to lease owner mismatch
- **Operational note**: Confirms store-level lease guard prevented lost update

### Management APIs

- **Kind**: `IInboxManager` / `IOutboxManager` dead-letter query and requeue (not OpenTelemetry)
- **When used**: Operator replay after inspecting failure reason and attempt count
- **Operational note**: `RequeueResult` reports partial requeue when some ids are not dead-lettered

### Stuck Processing Detection

- **Kind**: Queue depth gauge by status + runbook queries
- **When used**: `Processing`/`Publishing` rows with expired `lease_expires_at` should become reclaimable
- **Operational note**: Persistent stuck counts suggest store reclaim bug or clock skew

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (retry and dead letter)
- [Outbox](../../reliable-messaging/outbox.md) (retry and dead letter)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md) (lease recovery)
- [Production runbook](../../operations/runbook.md)

## Test Coverage

### Covered Use Cases

#### `InboxTests.ProcessPendingAsync_WhenHandlerThrows_ShouldMarkFailedAndSetVisibleAfter`

- **Use case**: When a handler throws on first failure, the row is marked failed with retry visibility
- **Test kind**: Unit
- **Description**: In-memory processor with throwing handler
- **Behavior**: Single dispatch failure
- **Expected outcome**: Failed status and `visible_after` set
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxTests.ProcessPendingAsync_WhenHandlerExceedsMaxAttempts_ShouldMoveToDeadLetter`

- **Use case**: When handler failures exhaust max attempts, the row dead-letters
- **Test kind**: Unit
- **Description**: Repeated failures until retry exhaustion
- **Behavior**: Multiple passes until max attempts
- **Expected outcome**: DeadLettered status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenFixedBackoffConfigured_ShouldSetVisibleAfterToInitialDelay`

- **Use case**: When fixed backoff is configured, retry visibility equals the initial delay
- **Test kind**: Unit
- **Description**: `RetryOptions` with fixed delay strategy
- **Behavior**: First failure persist
- **Expected outcome**: `visible_after` matches configured initial delay
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenExponentialBackoffConfigured_ShouldDoubleDelayPerAttempt`

- **Use case**: When exponential backoff is configured, retry delay doubles per attempt
- **Test kind**: Unit
- **Description**: Exponential backoff multiplier
- **Behavior**: Sequential failures with increasing attempt count
- **Expected outcome**: Delay doubles each attempt
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckCommand`

- **Use case**: When a lease expires on a stuck processing row, another pass reclaims and redispatches
- **Test kind**: Unit
- **Description**: Expired lease reclaim at processor level
- **Behavior**: Allow lease to expire then process again
- **Expected outcome**: Command redispatched
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxStoreContractTests.LeasePendingAsync_WhenFailedAndVisibleAfterReached_ShouldLeaseAgain`

- **Use case**: When a failed row becomes visible, lease store claims it again
- **Test kind**: Contract
- **Description**: Retry eligibility at store layer
- **Behavior**: Failed row with past `visible_after`
- **Expected outcome**: Row leased on next claim
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.LeasePendingAsync_ShouldIncrementAttemptCountAtLeaseTime`

- **Use case**: When a row is leased, attempt count increments at lease time
- **Test kind**: Contract
- **Description**: Lease metadata update
- **Behavior**: `LeasePendingAsync`
- **Expected outcome**: Attempt count increased by one
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.LeasePendingAsync_ShouldSetLeaseExpiresAtWhenProcessing`

- **Use case**: When a row enters processing, lease expiry timestamp is set
- **Test kind**: Contract
- **Description**: Active lease fields on claim
- **Behavior**: Lease transition to processing
- **Expected outcome**: `lease_expires_at` populated
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.LeasePendingAsync_ConcurrentWorkers_ShouldLeaseDisjointCommands`

- **Use case**: When multiple workers lease concurrently, each claims disjoint rows
- **Test kind**: Contract
- **Description**: Parallel lease workers
- **Behavior**: Concurrent `LeasePendingAsync` calls
- **Expected outcome**: No duplicate claims on same row
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.PersistAsync_WhenCompetingLeaseOwnersPersistConcurrently_ShouldApplyExactlyOnce`

- **Use case**: When competing lease owners persist terminal state concurrently, exactly one update wins
- **Test kind**: Contract
- **Description**: Conditional persist race at store layer
- **Behavior**: Concurrent terminal persist with different lease owners
- **Expected outcome**: Single successful terminal update
- **Remarks**: Store contract suite

#### `PostgreSqlInboxStorePersistResultTests.PersistAsync_when_lease_reclaimed_should_report_lease_lost`

- **Use case**: When persist runs with a stale lease owner, store reports lease lost
- **Test kind**: Integration
- **Description**: PostgreSQL conditional persist
- **Behavior**: Persist after lease reclaimed by another worker
- **Expected outcome**: Lease lost result reported to processor
- **Remarks**: `LiteBus.Inbox.Storage.PostgreSql.UnitTests`

#### `InboxManagerTests.RequeueAsync_WithMessageIds_ShouldReturnRowsToPending`

- **Use case**: When operator requeues by message ids, dead-lettered rows return to pending
- **Test kind**: Unit
- **Description**: Manager requeue API
- **Behavior**: `RequeueAsync` with specific ids
- **Expected outcome**: Pending status restored for matching dead letters
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxManagerRequeueTests.RequeueAsync_should_return_requested_and_requeued_counts`

- **Use case**: When requeue includes non-dead-letter ids, result reports requested vs requeued counts
- **Test kind**: Unit
- **Description**: Partial requeue reporting
- **Behavior**: `RequeueAsync` with mixed id set
- **Expected outcome**: `RequeueResult` distinguishes requested from requeued
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnEnvelopeToPending`

- **Use case**: When store requeue role runs, dead-lettered row returns to pending
- **Test kind**: Contract
- **Description**: Store-level dead letter requeue
- **Behavior**: `RequeueDeadLetterAsync`
- **Expected outcome**: Pending status
- **Remarks**: Store contract suite

#### `PipelinedInboxProcessorTests.PipelinedProcessor_WithHeartbeat_ShouldCompleteSlowHandlerWithoutReclaim`

- **Use case**: When heartbeat keeps the lease alive, a slow handler is not reclaimed mid-flight
- **Test kind**: Unit
- **Description**: Lease renewal during long dispatch
- **Behavior**: Heartbeat-enabled processor with slow handler
- **Expected outcome**: Single handler execution completes
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorCorrectnessTests.PipelinedProcessor_when_lease_renewal_fails_should_cancel_dispatch`

- **Use case**: When lease renewal fails, dispatch is canceled and a retryable failure is persisted
- **Test kind**: Unit
- **Description**: Heartbeat failure path
- **Behavior**: Simulated renewal failure during dispatch
- **Expected outcome**: Dispatch canceled; failed with retry visibility
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `EfCoreOutboxProcessorLeaseExpiryEndToEndTests.ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndPublishMessage`

- **Use case**: When an outbox lease expires on a publishing row, the processor reclaims and republishes
- **Test kind**: Integration
- **Description**: EF Core outbox lease expiry E2E
- **Behavior**: Stale publishing row after lease expiry
- **Expected outcome**: Message republished successfully
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Infinite retry without dead letter | No (by design) | N/A |: |: |
| Broker-native DLQ for inbox ingress | Partial | Ingress discard/requeue tested separately | Integration | Low |
| Global ordering across workers | No | N/A |: |: |

### Out-of-Scope Use Cases

- Broker-native dead-letter queue configuration
- Infinite retry policies
- Strict FIFO ordering guarantees across tenants
