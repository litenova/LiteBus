# Lease, Retry, and Dead Letter

**ID:** `durable-core.lease-retry-dead-letter`
**Maturity:** GA

## Summary

Leases, retries, and dead letters let processors claim work with time-bounded leases, renew leases during long handlers, retry failures with backoff, and move exhausted messages to dead letter for operator replay.

## What It Does

Lease stores atomically claim due rows with `lease_owner`, `lease_generation`, and `lease_expires_at`. Each claim increments the generation. Heartbeat renewal starts as soon as each envelope is leased, including while it waits for a worker slot, and continues through terminal persistence. PostgreSQL and relational EF Core stores calculate visibility and expiry from the database clock.

On failure, processors persist `Failed` with retry visibility computed from `RetryOptions`. When `AttemptCount` reaches `MaxAttempts`, rows move to `DeadLettered`. Operators requeue through `IInboxManager` / `IOutboxManager`. When the lease is lost during dispatch, the processor persists a retryable failure instead of leaving the row stuck in processing.

When the processor cancels a dispatch itself during shutdown, it releases the lease through `ILeaseRenewable.ReleaseLeaseAsync` with a `LeaseReleaseRequest`. The row returns to `Pending` (or to `Failed` when it already carries an error) with its lease cleared, its attempt count decremented, and its visibility unchanged. The release runs under a 5-second budget and logs EventId 3011 at Debug level. When the release fails, the processor falls back to lease expiry and logs EventId 3012 at Warning level.

Drain never cancels dispatch. The final pass runs with the uncancelled host token, so in-flight messages complete and persist normally.

## Permanent Failures

Processors dead-letter a message on its first failure when retrying the exception can never succeed. The built-in non-retryable set covers a missing handler (`NoHandlerFoundException`), multiple handlers (`MultipleHandlerFoundException`), a guard denial (`LiteBusMessageDeniedException`), and a validation failure (`LiteBusMessageInvalidException`). The processor applies that check through `MediationExceptionFilters.IsRetryableDispatchException`, which also inspects inner exceptions. Applications mark their own exception types as permanent with `RetryOptions.IsPermanentFailure`. That classifier runs in addition to the built-in set, and a match dead-letters the message on that attempt regardless of `MaxAttempts`.

```csharp
builder.UseProcessorOptions(new OutboxProcessorOptions
{
    Retry = new RetryOptions
    {
        IsPermanentFailure = exception => exception is UseCaseUnprocessableException
    }
});
```

The predicate receives the exception that ended the dispatch. Returning `true` dead-letters the message on that attempt. The processor does not call it for a cancellation caused by shutdown. When the predicate itself throws, the processor treats the result as `false`, so a faulty classifier keeps the retry schedule instead of losing the message. The same `Retry` property exists on `InboxProcessorOptions`, and both processors share this behavior.

## Public Surface

### Consumer Contracts

- **`ProcessorOptions`**: Serves as the shared base for inbox and outbox processor options, including lease and retry settings.
- **`RetryOptions`**: Carries `MaxAttempts` (default 5), `InitialDelay` (default 5 seconds), the `MaxDelay` cap (default 5 minutes), the backoff strategy (`Fixed`, `Exponential` by default, or `Schedule` with an explicit `Delays` ladder), jitter (on by default), and an optional `IsPermanentFailure` classifier that dead-letters matching failures on first failure. With `Schedule`, each attempt waits for the matching entry in `Delays`. An attempt past the end of the ladder reuses the last entry, and `MaxDelay` does not clamp ladder entries. `RetryOptions.Validate()` runs when the module builds and when a processor is constructed directly, so an empty or negative ladder is rejected at startup instead of aborting the first dispatch-failure pass.
- **`IInboxLeaseStore` / `IOutboxLeaseStore`**: Atomically claim due rows that are pending or failed-but-visible.
- **`LeaseRenewalRequest`**: The heartbeat input. It carries the envelope id, lease owner, fencing generation, extension duration, and an in-memory expiry fallback.
- **`LeaseReleaseRequest`**: The release input. It carries the message id, lease owner, and fencing generation for a dispatch the processor cancelled.
- **`ILeaseRenewable.ReleaseLeaseAsync`**: Returns a cancelled lease to the queue without counting the attempt the lease consumed. The default implementation returns `false`.
- **`IInboxManager` / `IOutboxManager`**: Provide the operator APIs for requeue and dead-letter query.
- **`RequeueResult`**: Reports how many rows were requested and how many were actually requeued, for partial requeue scenarios.

### Invocation

- **`LeasePendingAsync`**: Claims a batch with a new `lease_owner`, increments `lease_generation` and the attempt count, and sets `lease_expires_at`.
- **Heartbeat renewal** extends `lease_expires_at` while dispatch runs. When renewal fails, the processor cancels dispatch and schedules a retry.
- **Lease release** returns a dispatch the processor cancelled to `Pending` (or to `Failed` when it already carries an error) and decrements the attempt count. When the release fails, the processor falls back to lease expiry with the attempt counted.
- **Failure persist** sets `Failed` with `visible_after` from the backoff calculation.
- **Dead letter persist** transitions the row to `DeadLettered` when attempts are exhausted.
- **`RequeueAsync` / `RequeueDeadLettersAsync`**: The operator moves dead-lettered rows to `Pending`.

### Registration

- Storage adapters implement the lease store and conditional state writer roles.
- Processors register through **`EnableInboxProcessor`** / **`EnableOutboxProcessor`**, with **`UseProcessorOptions`** supplying lease and retry values.
- Managers register with the inbox and outbox modules for operator tooling.

### Configuration

- **`BatchSize`**: The maximum number of rows leased per pass (default 20).
- **`LeaseDuration`**: The claim time-to-live (default one minute). Rows stuck in processing become reclaimable after the lease expires.
- **`LeaseHeartbeatInterval`**: Sets the renewal cadence (default 15 seconds), which must be `<= LeaseDuration / 2`. `TimeSpan.Zero` disables heartbeat renewal.
- **`DispatcherConcurrency`**: The number of parallel dispatch workers per pass (default 1).
- **`Retry`**: Carries `MaxAttempts`, the backoff delay (fixed, exponential, or an explicit schedule), and optional jitter.
- **`LeaseOwner`**: An optional operator label; each processor instance appends an opaque random session suffix. The suffix fences out stale sessions that reuse the same configured label. When omitted, the processor builds a value from machine name, process id, and a random suffix.

### Extension Points

- A custom store must implement the atomic lease, renewal, conditional persist on `lease_owner` plus `lease_generation`, and requeue roles, and should implement `ReleaseLeaseAsync`. Without release support, a dispatch cancelled by shutdown still consumes an attempt.
- The hook failure policy can dead-letter a message before it reaches max attempts (see processor-hooks capability).
- Ingress poison handling is separate from processor dead letter (see ingress ack-policy capability).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `RetryOptions`, `ProcessorOptions` |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor lease orchestration |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Atomic lease and conditional persist |

## Requires

- Set `LeaseDuration > 0` so expired leases become reclaimable
- Use a store that supports conditional renewal and terminal persist on `lease_owner` plus `lease_generation`
- Keep an operator runbook for dead-letter inspection and requeue

## Invariants

- Another worker may lease the same row after lease expiry
- Terminal persist checks `lease_owner` and `lease_generation` to reject an older attempt, even when the same configured owner reacquires the row
- PostgreSQL and relational EF Core stores use database time for lease eligibility, expiry, and renewal
- `Retry.MaxAttempts` limits how many times a leased envelope is dispatched
- Requeue only moves rows from the dead-letter state, and partial requeues are reported in `RequeueResult`
- A heartbeat failure during dispatch leads to a retryable failed outcome, not indefinite processing
- An envelope renews its lease while it waits for a worker slot, not only during dispatch.
- When the caller token cancels a dispatch, `MediationExceptionFilters.IsCallerCancellation` detects it (even when a dispatcher wrapped the cancellation in its own exception type) and the processor records no outcome: no error and no visibility change.
- When the processor cancels a dispatch during shutdown, the store releases the lease without counting the attempt. When the store does not implement release, the lease expires with the attempt counted.
- Drain runs one final pass without cancelling dispatch, so in-flight messages complete and persist normally instead of releasing their leases.

## Non-Goals

- A broker-native DLQ replacement for inbox ingress. Ingress has a separate poison policy.
- Infinite retry without dead letter
- Global ordering guarantees across workers

## Observability

Lease and retry outcomes surface through processor counters on meters `LiteBus.Inbox` and `LiteBus.Outbox`. Register with `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`.

### `litebus.inbox.processor.failed` / `litebus.outbox.processor.failed`

- **Kind**: Counter
- **When emitted**: Each envelope marked failed with retry visibility after a dispatch or lease-loss failure
- **Operational note**: Compare the retry rate with the succeeded and published rates. Spikes indicate handler or broker instability

### `litebus.inbox.processor.dead_lettered` / `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: Retry exhaustion or the hook policy moves the row to dead letter
- **Operational note**: Treat this as the primary alert for operator intervention, and follow the manager requeue workflows

### `litebus.inbox.processor.lease_lost` / `litebus.outbox.processor.lease_lost`

- **Kind**: Counter (`ProcessorLeaseLostInstrumentName`)
- **When emitted**: Heartbeat renewal fails and the processor cancels dispatch
- **Operational note**: Tune the lease duration and heartbeat when this correlates with slow handlers

### `litebus.inbox.processor.persist_skipped` / `litebus.outbox.processor.persist_skipped`

- **Kind**: Counter
- **When emitted**: Terminal persist is skipped because the lease was reclaimed by another worker
- **Operational note**: This is expected under concurrent workers. The row becomes visible again instead of staying stuck

### `litebus.inbox.processor.persist_rejected` / `litebus.outbox.processor.persist_rejected`

- **Kind**: Counter
- **When emitted**: The conditional persist rejects the update because of a lease owner mismatch
- **Operational note**: Confirms that the store-level lease guard prevented a lost update

### Management APIs

- **Kind**: Dead-letter query and requeue through `IInboxManager` / `IOutboxManager` (not OpenTelemetry)
- **When used**: Operator replay after inspecting the failure reason and attempt count
- **Operational note**: `RequeueResult` reports partial requeue when some ids are not dead-lettered

### Stuck Processing Detection

- **Kind**: The queue depth gauge by status, plus runbook queries
- **When used**: `Processing`/`Publishing` rows with expired `lease_expires_at` should become reclaimable
- **Operational note**: Persistent stuck counts suggest a store reclaim bug or database clock issue. Caller clock skew does not change relational lease eligibility.

## References

- [Inbox](../../reliable-messaging/inbox.md) (how retry and dead letter work)
- [Outbox](../../reliable-messaging/outbox.md) (how retry and dead letter work)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md) (how lease recovery works)
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
- **Expected outcome**: Dispatch canceled, and failed with retry visibility
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `EfCoreOutboxProcessorLeaseExpiryEndToEndTests.ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndPublishMessage`

- **Use case**: When an outbox lease expires on a publishing row, the processor reclaims and republishes
- **Test kind**: Integration
- **Description**: EF Core outbox lease expiry E2E
- **Behavior**: Stale publishing row after lease expiry
- **Expected outcome**: Message republished successfully
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `PostgreSqlInboxStoreTests.LeasePendingAsync_WhenCallerClockIsSkewed_ShouldUseDatabaseClock`

- **Use case**: A caller supplies a timestamp ten years ahead of the database clock
- **Test kind**: Integration
- **Description**: Direct PostgreSQL lease selection and expiry calculation
- **Behavior**: One future-visible row and one ready row are leased with the skewed request
- **Expected outcome**: Only the ready row is leased and its expiry is based on database time
- **Remarks**: The EF Core PostgreSQL inbox and both outbox stores run the same contract assertion

#### `InboxStoreContractTests.LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand`

- **Use case**: One configured owner reacquires its row after lease expiry
- **Test kind**: Contract
- **Description**: Generation fencing after reclaim
- **Behavior**: The stale and current envelopes attempt terminal persistence
- **Expected outcome**: Only the current generation persists terminal state
- **Remarks**: The outbox contract suite runs the equivalent assertion

#### `InboxPermanentFailureTests.ProcessPendingAsync_WhenClassifierMarksFailurePermanent_ShouldDeadLetterOnFirstAttempt`

- **Use case**: When `IsPermanentFailure` recognizes the failure, the inbox row dead-letters on that attempt
- **Test kind**: Unit
- **Description**: Inbox processor with a matching permanent-failure classifier
- **Behavior**: Single dispatch failure recognized by the classifier
- **Expected outcome**: DeadLettered status regardless of `MaxAttempts`
- **Remarks**: `LiteBus.Inbox.UnitTests`. The outbox twin is `OutboxPermanentFailureTests.ProcessPendingAsync_WhenClassifierMarksFailurePermanent_ShouldDeadLetterOnFirstAttempt`

#### `InboxPermanentFailureTests.ProcessPendingAsync_WhenClassifierThrows_ShouldScheduleRetry`

- **Use case**: When the permanent-failure classifier throws, the retry schedule is kept
- **Test kind**: Unit
- **Description**: Inbox processor with a throwing classifier
- **Behavior**: Dispatch failure with a faulty classifier
- **Expected outcome**: Failed status with retry visibility instead of dead letter
- **Remarks**: `LiteBus.Inbox.UnitTests`. The outbox twin is `OutboxPermanentFailureTests.ProcessPendingAsync_WhenClassifierThrows_ShouldScheduleRetry`

#### `RetryScheduleTests.A_schedule_returns_each_delay_in_order`

- **Use case**: When `Schedule` backoff is configured, each attempt waits its ladder entry
- **Test kind**: Unit
- **Description**: `RetryOptions` with `Backoff = RetryBackoff.Schedule` and explicit `Delays`
- **Behavior**: `CalculateDelay` per attempt number
- **Expected outcome**: Each delay in ladder order, and attempts past the end repeat the last entry
- **Remarks**: `LiteBus.Outbox.UnitTests`. Companion tests cover the unclamped tail, the jitter bound, and the refused empty or negative ladder

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Infinite retry without dead letter | No (by design) | Not supported | N/A | N/A |
| Broker-native DLQ for inbox ingress | Partial | Ingress discard/requeue is tested separately | Integration | Low |
| Global ordering across workers | No | Not supported | N/A | N/A |

### Out-of-Scope Use Cases

- Broker-native dead-letter queue configuration
- Infinite retry policies
- Strict FIFO ordering guarantees across tenants
