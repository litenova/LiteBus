# Message States and Alerting

Every durable message sits in a stored row whose status names its current owner. The owner is the writer, the leased processor, or nobody once the row reaches a terminal value. Inbox rows move through `InboxStatus`. Outbox rows move through `OutboxStatus`. This page is the operator reference for those values and the alerts to set on them. [Reliable Messaging Semantics](../reliable-messaging/semantics.md) defines the delivery guarantees. [Ordering and Concurrency Guarantees](../reliable-messaging/ordering.md) defines the concurrency properties. [Envelope Lifecycle](../catalog/durable-core/envelope-lifecycle.md) holds the test evidence behind the transitions.

## Outbox States (`OutboxStatus`)

`OutboxStatus` is defined in `LiteBus.Outbox.Abstractions`. Writers create `Pending` rows through `IOutbox.EnqueueAsync`. `PipelinedOutboxProcessor` leases due rows and persists terminal outcomes through `IOutboxStateWriter.PersistAsync`.

| Status | What it means | Produced by | Picked up again | Alert guidance |
| --- | --- | --- | --- | --- |
| `Pending` | Stored and awaiting lease. The receipt (`Enqueued` or `AlreadyEnqueued`) confirms storage, not publication. | `IOutbox.EnqueueAsync` (or `ITransactionalOutbox` variants), and `OutboxEnvelope.AsRequeued` through `IOutboxManager.RequeueAsync` or `IOutboxManager.RequeueDeadLettersAsync`. | Yes, when due (`VisibleAfter` is null or past). | Alert on count growth and on the age of the oldest row. Oldest-pending age measures publication lag directly. |
| `Publishing` | Leased by one processor instance. `LeaseOwner`, `LeaseExpiresAt`, `LeaseGeneration`, and `LastAttemptedAt` name the holder. | Stores lease due rows through `IOutboxLeaseStore.LeasePendingAsync` during a processor pass (`OutboxEnvelope.AsLeased`, which also adds one to `AttemptCount`). | Only after the lease expires, when another pass reclaims the row. Never while the lease is valid. | Alert when rows stay in `Publishing` past `OutboxProcessorOptions.LeaseDuration` with no heartbeat renewal. That pattern means a crashed holder or a dispatch longer than the lease. |
| `Published` | Dispatched successfully. Terminal. | `OutboxEnvelope.AsPublished` after `IOutboxDispatcher.DispatchAsync` returns, persisted by `OutboxPipelinedMessageProcessorOperations.PersistTerminalOutcomeAsync`. | No. | Do not alert on this state. Watch retention instead. `IOutboxManager.GetRetentionStatusAsync` and `RunRetentionPurgeAsync` report and run cleanup of aged published rows. |
| `Failed` | Dispatch failed and a retry is scheduled at `VisibleAfter`. `LastError` and `ErrorType` record the failure. | `OutboxEnvelope.AsFailed` on a retryable exception, with `VisibleAfter` from `RetryOptions.CalculateDelay`. The lease-loss path in `PersistLeaseLossOutcomeAsync` also produces it. It records `MessageFailure.FromError` when the lease was lost mid-dispatch. | Yes, once `VisibleAfter` passes. | Treat as an early warning, not a page. A short spike that drains is normal retry behavior. Page when `Failed` grows together with dead letters, or when one `ErrorType` dominates. |
| `DeadLettered` | Retry policy exhausted, or the message was retired on first failure, or an `AfterDispatch` hook failed under `ProcessorHookFailurePolicy.DeadLetter`. Terminal for the processor. | `OutboxEnvelope.AsDeadLettered` when `AttemptCount >= Retry.MaxAttempts`, when `MediationExceptionFilters.IsRetryableDispatchException` returns false, or when the hook policy dead-letters. Refusals include `LiteBusMessageDeniedException`, `LiteBusMessageInvalidException`, and missing-handler configuration errors, including wrapped inner exceptions. The enum also covers rows manually moved aside. | No. Only an operator requeues, through `IOutboxManager.RequeueAsync` or `RequeueDeadLettersAsync`, which reset the row to `Pending` with `AttemptCount` 0. | Page on this. Alert on the dead-letter count (per contract or per `ErrorType`) and on new dead letters appearing. Inspect with `IOutboxManager.QueryAsync` and `GetMessageAsync` before replaying. |

## Inbox States (`InboxStatus`)

`InboxStatus` is defined in `LiteBus.Inbox.Abstractions`. Writers create `Pending` rows through `IInbox.AcceptAsync`. `PipelinedInboxProcessor` leases due rows and persists terminal outcomes through `IInboxStateWriter.PersistAsync`. The shape mirrors the outbox table above. Only the actors differ.

| Status | What it means | Produced by | Picked up again | Alert guidance |
| --- | --- | --- | --- | --- |
| `Pending` | Accepted and awaiting execution. The receipt (`Accepted` from `InboxAcceptOutcome`, or `AlreadyAccepted` on idempotent resubmission) confirms storage, not execution. | `IInbox.AcceptAsync` (or `ITransactionalInbox` variants), and `InboxEnvelope.AsRequeued` through `IInboxManager.RequeueAsync` or `IInboxManager.RequeueDeadLettersAsync`. | Yes, when due. | Alert on count growth and oldest-row age. A growing `Pending` count with flat `Processing` means the processor is behind or stopped. |
| `Processing` | Leased by one processor instance. Same lease fields as outbox `Publishing`. | Stores lease due rows through `IInboxLeaseStore.LeasePendingAsync` during a processor pass (`InboxEnvelope.AsLeased`, which also adds one to `AttemptCount`). | Only after the lease expires, when another pass reclaims the row. | Alert when rows stay in `Processing` past `InboxProcessorOptions.LeaseDuration`. Check `LastLeaseOwner` to find the stuck holder. Then follow the [Production Runbook](runbook.md) for stuck rows. |
| `Completed` | Handler dispatch succeeded. Terminal. | `InboxEnvelope.AsCompleted` after `IInboxDispatcher.DispatchAsync` returns, persisted by `InboxPipelinedMessageProcessorOperations.PersistTerminalOutcomeAsync`. A suppressed hook dispatch (`ShouldDispatch` false) also completes without invoking the dispatcher. | No. | Do not alert on this state. Aged completed rows are removed by retention (`DeleteCompletedOlderThanAsync`, reported by `IInboxManager.GetRetentionStatusAsync`). |
| `Failed` | Dispatch failed and a retry is scheduled at `VisibleAfter`. | `InboxEnvelope.AsFailed` on a retryable exception, with `VisibleAfter` from `RetryOptions.CalculateDelay`. The lease-loss path also produces it. It records a failure with no `ErrorType` because no exception exists. `FirstFailedAt` is set on the first failure only. | Yes, once `VisibleAfter` passes. | Early warning only. Page when `Failed` accumulates or feeds dead letters. |
| `DeadLettered` | Retry policy exhausted or retired on first failure. Terminal for the processor. | `InboxEnvelope.AsDeadLettered` under the same rules as the outbox (`AttemptCount >= Retry.MaxAttempts`, a non-retryable exception, or `ProcessorHookFailurePolicy.DeadLetter` on `AfterDispatch` failure). | No. Only an operator requeues, which resets the row to `Pending` with `AttemptCount` 0 and clears `FirstFailedAt`, `DeadLetteredAt`, `ErrorType`, and `LastError`. | Page on this. Query with `InboxMessageFilter.DeadLettered` or `IInboxManager.QueryAsync`. Fix the cause, then replay with `RequeueAsync`. |

## Attempt Count Semantics

Leasing a row charges the attempt. `InboxEnvelope.AsLeased` and `OutboxEnvelope.AsLeased` add one to `AttemptCount`. The lease update writes `attempt_count + 1`, so a process that crashes mid-dispatch still spends an attempt. `AsFailed` and `AsDeadLettered` do not add to it.

A dispatch cancelled by the caller's token is not a failure. `InboxProcessorEnvelopeHandler` and `OutboxProcessorEnvelopeHandler` return no outcome through the `MediationExceptionFilters.IsCallerCancellation` filter, so no error is recorded and no retry is scheduled.

When the cancellation came from the processor's own pass token (host shutdown or a drain), the processor also calls `ILeaseRenewable.ReleaseLeaseAsync`. The release returns the row to `Pending` (or `Failed` when it already carries `LastError`), clears the lease, and decrements `AttemptCount`. A shutdown therefore leaves the counter where it was. When a release fails or exceeds its 5-second budget, the failure is logged (EventId 3012). The lease expires instead, with the attempt counted. The processor does not release a cancellation caused by a lost lease, because another processor may already own the row.

`AttemptCount` therefore counts leases that ended in an outcome or a crash, not failures.

Alert on the dead-letter count and on the age of the oldest pending message rather than on `attempt_count >= N` heuristics, for two reasons. First, a fast-failing message reaches dead letter through the retry policy on its own. `AttemptCount >= Retry.MaxAttempts` already dead-letters. Non-retryable messages dead-letter on the first attempt. A count heuristic therefore duplicates the policy and pages on messages the processor already retired. Second, the counter cannot distinguish a message failing every attempt from one leased repeatedly after crashes or lost leases. Dead-letter count names messages that gave up. Oldest-pending age names lag. Both are actionable, while an attempt-count threshold is neither.

```csharp
var counts = await outboxManager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false);
var deadLettered = counts.TryGetValue(OutboxStatus.DeadLettered, out var n) ? n : 0;

var page = await outboxManager.QueryAsync(
    new OutboxMessageFilter { Statuses = [OutboxStatus.Pending] },
    new OutboxMessagePageRequest { PageSize = 100 },
    cancellationToken).ConfigureAwait(false);
var oldestPendingAge = page.Items.Count == 0
    ? TimeSpan.Zero
    : DateTimeOffset.UtcNow - page.Items.Min(m => m.CreatedAt);
```

## Query and Page Messages

`IInboxManager.QueryAsync` and `IOutboxManager.QueryAsync` take a filter and a page request. Every filter field is optional. Unset fields do not narrow the result.

| Field | Inbox | Outbox |
| --- | --- | --- |
| `MessageId`, `MessageIds` | Yes | Yes |
| `Statuses` | `InboxStatus` values | `OutboxStatus` values |
| `ContractName` | Yes | Yes |
| `Topic` | No | Yes |
| `CorrelationId`, `CausationId` | Yes | Yes |
| `TenantId` | Yes | Yes |
| `CreatedAfter`, `CreatedBefore` | Inclusive `CreatedAt` bounds | Inclusive `CreatedAt` bounds |

`InboxMessageFilter.DeadLettered` and `OutboxMessageFilter.DeadLettered` are presets that match only dead-lettered rows. Page requests carry `PageSize` (default 50) and an opaque keyset `Cursor`. Each page returns its items with `HasMore` and `NextCursor`. Pass `NextCursor` as the next `Cursor` until `HasMore` is false.

```csharp
var filter = InboxMessageFilter.DeadLettered;
string? cursor = null;

do
{
    var page = await inboxManager.QueryAsync(
        filter,
        new InboxMessagePageRequest { PageSize = 50, Cursor = cursor },
        cancellationToken).ConfigureAwait(false);

    foreach (var envelope in page.Items)
    {
        // Inspect envelope.ErrorType, envelope.LastError, and envelope.AttemptCount.
    }

    cursor = page.HasMore ? page.NextCursor : null;
}
while (cursor is not null);
```

Two more manager calls support operations. `GetSchemaInfoAsync` returns `StoreSchemaInfo`, which carries the store component name, the expected and recorded schema versions, and the schema and table names for relational backends. Poll it after deploys and migrations to confirm the store matches the running release. `GetRetentionStatusAsync` returns `RetentionRunStatus`, which reports whether the cleanup loop is enabled, the configured retention period and interval, and the timestamp, deleted count, and error of the last pass. When terminal rows accumulate, poll it to see whether cleanup runs and what its last pass did.

`IOutboxManager.GetFailureSummaryAsync` returns per-contract failing counts with the latest error, which separates messages that fail on every attempt from a backlog that is merely slow.

## Retention Cutoff

Cleanup services call `IInboxRetentionStore.DeleteCompletedOlderThanAsync` and `IOutboxRetentionStore.DeletePublishedOlderThanAsync` with a cutoff timestamp. Implementations compare the cutoff against the effective terminal timestamp:

| Store | Effective timestamp | SQL expression |
| --- | --- | --- |
| Inbox | Completion time when recorded, otherwise acceptance time | `COALESCE(completed_at, created_at)` |
| Outbox | Publication time when recorded, otherwise enqueue time | `COALESCE(published_at, created_at)` |

Inbox and outbox schemas include `completed_at`, `published_at`, and operational history columns such as `last_attempted_at` and `dead_lettered_at`. Retention deletes rows by finish time, not accept time. A message accepted long ago but completed recently stays until the cutoff passes its completion time.

Retention runs either on the `EnableCleanup` background loop or on demand through `RunRetentionPurgeAsync` (and `POST .../retention/purge` on the [Management Endpoints](management-endpoints.md)). Before purging by hand, use `GetRetentionStatusAsync` to confirm the loop policy and its last outcome.

## Management and Observability Surfaces

Two surfaces expose counts and rows. Use the manager APIs for point-in-time inspection and the meters for trends.

| Surface | Members | Use |
| --- | --- | --- |
| `IInboxManager` / `IOutboxManager` | `GetStatusCountsAsync`, `QueryAsync`, `GetMessageAsync` | Dashboards, runbooks, and health probes read live counts and rows here. These are authoritative, while the gauges below are cached for export efficiency. |
| `IInboxManager` / `IOutboxManager` | `RequeueAsync`, `RequeueDeadLettersAsync`, `PurgeAsync`, `RunRetentionPurgeAsync`, `GetRetentionStatusAsync`, `GetSchemaInfoAsync` | Replay, purge, retention, and schema inspection. The same operations back the HTTP management endpoints ([Management Endpoints](management-endpoints.md)). |
| `LiteBusInboxTelemetry` / `LiteBusOutboxTelemetry` | `QueueDepthInstrumentName` (`litebus.inbox.queue.depth`, `litebus.outbox.queue.depth`) tagged by `QueueStatusAttributeName` (`litebus.inbox.status`, `litebus.outbox.status`) | Queue-depth trends per status. Rising `Pending` with flat `Processing` or `Publishing` indicates processor lag. |
| `LiteBusInboxTelemetry` / `LiteBusOutboxTelemetry` | `ProcessorLeaseLostInstrumentName`, `ProcessorPersistSkippedInstrumentName`, `ProcessorPersistRejectedInstrumentName`, `ProcessorPersistFailedInstrumentName`, `CleanupErrorInstrumentName` | Lease fencing, skipped or rejected terminal persists, and retention cleanup failures. Alert when cleanup errors persist, because terminal rows accumulate without purge. |

Wire trend alerts to the queue-depth gauges and dead-letter counts. Link runbook pages to `GetStatusCountsAsync` with a first-page `QueryAsync`, so the on-call engineer starts from the oldest row, not from an attempt count. [Diagnostics and Health](diagnostics-and-health.md) covers probe wiring.
