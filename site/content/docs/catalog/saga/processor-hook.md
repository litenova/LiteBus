# Saga Processor Hook

- **ID**: `saga.processor-hook`
- **Name**: Saga Processor Hook
- **Maturity**: Extension
- **Summary**: Loads saga state before inbox dispatch and saves or completes it after successful dispatch.

## What It Does

`SagaProcessorHook` implements `IProcessorEnvelopeHook` for saga. On `BeforeDispatchAsync` it resets ambient scope, resolves the saga definition from the message contract, builds a `SagaCorrelation`, loads existing state from `ISagaStore`, and begins an active scope. Completed sagas skip active scope so further correlated messages dispatch with an inactive scope and without saga mutation; only a redelivery whose message id matches `LastAppliedMessageId` suppresses dispatch.

On `PrepareDispatchScope` and at the start of `AfterDispatchAsync`, the hook attaches the scope identified by `IProcessorEnvelope.MessageId`. The synchronous prepare phase sets `AsyncLocal` state on the same logical flow that calls the dispatcher. Message IDs also prevent concurrent deliveries for one saga correlation from overwriting each other's state snapshots.

On `AfterDispatchAsync`, the hook compares the current state with the JSON snapshot taken when it was loaded. If the handler marked state dirty through `SetState` or changed the loaded instance in place, the hook saves once with the observed version and propagates `SagaConcurrencyException` on conflict. It does not reload and replace a newer row with a stale handler-owned snapshot. A dispatch that only reads state saves nothing, so reading state and then calling `Complete()` completes the saga. If the handler called `Complete()`, the hook records completion and may reload and retry up to three attempts because completion is idempotent. Handler-thrown exceptions skip save. The processor calls `AfterDispatchAsync` on the persist token before inbox terminal persistence, so a shutdown during the saga save does not leave an executed command to run again. The processor then calls `AbandonDispatchScope`, which removes the keyed context entry before retry or dead-letter handling.

Dispatches for the same tenant, saga definition, and correlation identifier are serialized inside one hook instance. The gate covers state load, handler dispatch, hook persistence, and scope cleanup, so two workers in one process cannot both load the same saga version. Separate processes still require the store's optimistic concurrency checks.

Each save and completion records the inbox message identifier in `last_applied_message_id` when the store supports the optional field. A repeated delivery with that identifier skips the handler scope after a crash between saga persistence and inbox terminal persistence. The inbox row and saga row use separate transactions, so the message can still be retried after a process failure; the ledger makes that retry a no-op for the saga state.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaProcessorHook.BeforeDispatchAsync(...)` | `LiteBus.Saga` | Resolve mapping, build correlation, load state, begin scope |
| `SagaProcessorHook.PrepareDispatchScope(...)` | `LiteBus.Saga` | Attach the message-keyed scope before dispatcher execution |
| `SagaProcessorHook.ShouldDispatch(...)` | `LiteBus.Saga` | Skip the dispatcher only when the message id matches `LastAppliedMessageId`; new messages on completed sagas dispatch with an inactive scope |
| `SagaProcessorHook.AbandonDispatchScope(...)` | `LiteBus.Saga` | Remove state after canceled or failed dispatch |
| `SagaProcessorHook.AfterDispatchAsync(...)` | `LiteBus.Saga` | Save dirty state once or complete saga with bounded retry |
| Registration via `EnableSaga()` | `LiteBus.Saga.InboxIntegration` | Adds hook as `IProcessorEnvelopeHook` singleton |

## Packages

- `LiteBus.Saga`
- `LiteBus.DurableMessaging.Abstractions` (`IProcessorEnvelopeHook` in `LiteBus.Messaging.Abstractions.Processing`)
- `LiteBus.Saga.Abstractions` (store and registry dependencies)

## Requires

- `durable-core.processor-hooks`
- `saga.store` (or `saga.in-memory-store` / `saga.postgresql-storage`)
- `saga.state-registration`
- `saga.correlation-and-tenancy`
- Inbox processor with dispatch (`inbox.*` axis)

## Invariants

- No correlation id on envelope: hook no-ops; no saga scope.
- Unmapped contract name: hook no-ops; no saga scope.
- Handler throw: `AfterDispatchAsync` save path not reached for that failed dispatch attempt.
- Failed and canceled dispatches release the message-keyed scope before the processor returns an outcome.
- Parallel messages with the same saga correlation retain separate ambient snapshots.
- Messages with the same saga correlation are serialized per hook instance before state load.
- Dirty-state conflicts propagate without automatic state merge.
- Completion-only conflicts may retry up to three attempts.
- `SetState` and `Complete` in the same dispatch throw `InvalidOperationException` before persist.
- State changes are detected by comparing serialized state with the load snapshot; in-place mutations persist without `SetState`, and a read-only dispatch saves nothing.
- Only a `LastAppliedMessageId` match suppresses dispatch; new messages on a completed saga dispatch with an inactive scope and no saga mutation.
- `Complete()` with no prior save stores the initial snapshot first, then completes the new row instead of reporting a concurrency conflict.
- Saga save runs after successful handler dispatch but before inbox terminal persistence.
- Inbox terminal state and saga rows are separate persistence steps. The applied-message ledger closes the duplicate-application window without claiming cross-store atomicity.

## Non-Goals

- Does not dispatch the next saga step (application enqueues the next correlated inbox command).
- Does not compensate or roll back side effects.
- Does not participate in outbox event dispatch.

## Observability

The saga hook does not emit dedicated meters or activities.

**What to use instead**

- Inbox processor metrics (`litebus.inbox.processor.succeeded`, `failed`, `dead_lettered`) for dispatch outcomes.
- Application logging on dirty-state `SagaConcurrencyException` (include message id and saga definition id).
- PostgreSQL row growth and `QueryAsync` for operational visibility when using durable storage.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [optimistic-concurrency](optimistic-concurrency.md)
- [store](store.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException`

- **Use case**: Hook rejects simultaneous dirty state and completion in one dispatch.
- **Test kind**: Unit
- **Description**: Direct hook invocation after `BeforeDispatchAsync` with both `SetState` and `Complete` on active scope.
- **Behavior**: `AfterDispatchAsync` persist guard.
- **Expected outcome**: `InvalidOperationException`; no store write.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenDispatchOnlyReadsState_SkipsSave`

- **Use case**: A dispatch that reads state without changing it performs no store write.
- **Test kind**: Unit
- **Description**: Handler calls `GetState` and returns the snapshot unchanged.
- **Behavior**: The hook compares current state with the load snapshot and finds no changes.
- **Expected outcome**: No save and no row created for the new saga.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenStateIsReadThenCompleted_CompletesSaga`

- **Use case**: Reading state and then calling `Complete()` completes the saga.
- **Test kind**: Unit
- **Description**: Handler reads state, changes nothing, and calls `Complete()`.
- **Behavior**: A read carries no changes, so completion is not combined with a state change.
- **Expected outcome**: The saga row completes without a concurrency conflict.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenStateChangesInPlace_PersistsWithoutSetState`

- **Use case**: In-place mutation of the `GetState` instance persists without `SetState`.
- **Test kind**: Unit
- **Description**: Handler mutates the returned state object and never calls `SetState`.
- **Behavior**: The serialized state differs from the load snapshot, so the hook saves.
- **Expected outcome**: The stored row carries the mutation at the next version.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenInPlaceChangeCombinesWithComplete_ThrowsInvalidOperationException`

- **Use case**: An in-place change combined with `Complete()` is rejected like `SetState` plus `Complete()`.
- **Test kind**: Unit
- **Description**: Handler mutates state in place and calls `Complete()` in one dispatch.
- **Behavior**: Change detection reports changes, so the persist guard throws.
- **Expected outcome**: `InvalidOperationException`; no store write.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Successful inbox dispatch persists handler mutations through hook after phase.
- **Test kind**: Unit
- **Description**: End-to-end inbox processor pass with `SagaProcessorHook` and in-memory stores.
- **Behavior**: Handler increments saga step via `ISagaContext`.
- **Expected outcome**: Store row with `Step == 1`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.ProcessPendingAsync_ConcurrentCompletionRequests_ShouldCompleteSagaOnce`

- **Use case**: Completion-only conflicts converge after concurrent inbox dispatch.
- **Test kind**: Unit
- **Description**: Two correlated messages dispatch concurrently and request `ISagaContext.Complete()` against version 1.
- **Behavior**: Message-keyed scopes remain isolated; the stale hook reloads the completed row and resets without a second completion update.
- **Expected outcome**: Both inbox messages complete, and the saga row reaches completed version 2 once.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenDirtySaveConflicts_PropagatesAfterSingleAttempt`

- **Use case**: Dirty state cannot be merged safely after a concurrent version advance.
- **Test kind**: Unit
- **Description**: A version-checking store rejects the handler-owned dirty snapshot.
- **Behavior**: The hook performs one save and does not reload or reapply the stale state.
- **Expected outcome**: `SagaConcurrencyException` propagates and the message-keyed scope is released.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenEveryCompletionAttemptConflicts_ThrowsAfterThreeAttempts`

- **Use case**: Completion-only retry is finite under sustained concurrent updates.
- **Test kind**: Unit
- **Description**: Every completion write conflicts after the hook reloads the current active version.
- **Behavior**: The hook stops after three completion attempts.
- **Expected outcome**: `SagaConcurrencyException` propagates and the scope is released.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Hook persists to PostgreSQL after in-process inbox dispatch.
- **Test kind**: Integration
- **Description**: `EnableSaga` + `UsePostgreSqlStorage` composition.
- **Behavior**: Single correlated command through full hook pipeline.
- **Expected outcome**: PostgreSQL saga row with expected state snapshot.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Hook load/save advances one saga instance across two correlated inbox messages.
- **Test kind**: Integration
- **Description**: Reserve then capture workflow commands sharing correlation id.
- **Behavior**: Two processor passes; hook reloads version 1 state before second dispatch.
- **Expected outcome**: Version 2 row; both workflow flags true; single physical row.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Handler failure skips hook save; compensation message restores prior saga snapshot.
- **Test kind**: Integration
- **Description**: Failure gate on capture step; later compensate command.
- **Behavior**: Failed capture leaves saga at version 1; compensation reloads and saves rollback state.
- **Expected outcome**: `Compensated == true`; inventory released; capture inbox row failed.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: Hook optimistic concurrency prevents lost updates under parallel workers.
- **Test kind**: Integration
- **Description**: Four processors, two correlated increment messages, delayed handler.
- **Behavior**: Stale dirty state propagates `SagaConcurrencyException` without replacing the winning row.
- **Expected outcome**: State and version advance together; the stale inbox message follows failure policy instead of reporting a lost update as completed.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition hook persists saga state across two correlated commands.
- **Test kind**: Composition
- **Description**: `AddV6CompositionSmoke` smoke with saga handlers in `LiteBus.Runtime.UnitTests`.
- **Behavior**: Two accepts, two processor passes.
- **Expected outcome**: `Step == 2` on loaded instance.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Outbox saga processor hook.
- Automatic next-step dispatch.
- Transactional inbox terminal update and saga save in one database transaction.
