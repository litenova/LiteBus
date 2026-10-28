# Saga Processor Hook

- **ID**: `saga.processor-hook`
- **Name**: Saga Processor Hook
- **Maturity**: Extension
- **Summary**: The hook loads saga state before inbox dispatch and saves or completes it after successful dispatch.

## What It Does

`SagaProcessorHook` implements `IProcessorEnvelopeHook` for saga. On `BeforeDispatchAsync`, it resets the ambient scope. It then resolves the saga definition from the message contract, builds a `SagaCorrelation`, loads existing state from `ISagaStore`, and begins an active scope. Completed sagas skip the active scope, so further correlated messages dispatch with an inactive scope and without saga mutation. Only a redelivery whose message id matches `LastAppliedMessageId` suppresses dispatch.

On `PrepareDispatchScope` and at the start of `AfterDispatchAsync`, the hook attaches the scope identified by `IProcessorEnvelope.MessageId`. The synchronous prepare phase sets `AsyncLocal` state on the same logical flow that calls the dispatcher. Because scopes are keyed by message id, concurrent deliveries for one saga correlation cannot overwrite each other's state snapshots.

On `AfterDispatchAsync`, the hook compares the current state with the JSON snapshot taken when it was loaded. If the handler marked state dirty through `SetState` or changed the loaded instance in place, the hook saves once with the observed version. On conflict, it propagates `SagaConcurrencyException` instead of reloading and replacing a newer row with a stale handler-owned snapshot.

A dispatch that only reads state saves nothing, so reading state and then calling `Complete()` completes the saga. If the handler called `Complete()`, the hook records completion. Because completion is idempotent, it may reload and retry up to 3 attempts.

When the handler throws, the hook skips the save. The processor calls `AfterDispatchAsync` on the persist token before inbox terminal persistence, so a shutdown during the saga save does not leave an executed command to run again. The processor then calls `AbandonDispatchScope`, which removes the keyed context entry before retry or dead-letter handling.

One hook instance serializes dispatches for the same tenant, saga definition, and correlation identifier. The gate covers state load, handler dispatch, hook persistence, and scope cleanup, so two workers in one process cannot both load the same saga version. Separate processes still require the store's optimistic concurrency checks.

When the store supports the optional field, each save and completion records the inbox message identifier in `last_applied_message_id`. A repeated delivery with that identifier skips the handler scope after a crash between saga persistence and inbox terminal persistence. The inbox row and saga row use separate transactions, so the message can still be retried after a process failure. The applied-message ledger makes that retry leave the saga state unchanged.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaProcessorHook.BeforeDispatchAsync(...)` | `LiteBus.Saga` | Resolve mapping, build correlation, load state, begin scope |
| `SagaProcessorHook.PrepareDispatchScope(...)` | `LiteBus.Saga` | Attach the message-keyed scope before dispatcher execution |
| `SagaProcessorHook.ShouldDispatch(...)` | `LiteBus.Saga` | Skip the dispatcher only when the message id matches `LastAppliedMessageId`. New messages on completed sagas dispatch with an inactive scope |
| `SagaProcessorHook.AbandonDispatchScope(...)` | `LiteBus.Saga` | Remove state after canceled or failed dispatch |
| `SagaProcessorHook.AfterDispatchAsync(...)` | `LiteBus.Saga` | Save dirty state once or complete saga with bounded retry |
| Registration through `EnableSaga()` | `LiteBus.Saga.InboxIntegration` | Adds the hook as an `IProcessorEnvelopeHook` singleton |

## Packages

- `LiteBus.Saga`
- `LiteBus.DurableMessaging.Abstractions` (`IProcessorEnvelopeHook` in `LiteBus.Messaging.Abstractions.Processing`)
- `LiteBus.Saga.Abstractions`, which carries the store and registry dependencies.

## Requires

- `durable-core.processor-hooks`
- `saga.store` through `saga.in-memory-store` or `saga.postgresql-storage`.
- `saga.state-registration`
- `saga.correlation-and-tenancy`
- An inbox processor with dispatch (`inbox.*` axis).

## Invariants

- When the envelope has no correlation id, the hook does nothing and creates no saga scope.
- When the contract name is unmapped, the hook does nothing and creates no saga scope.
- When the handler throws, the `AfterDispatchAsync` save path is not reached for that failed dispatch attempt.
- Failed and canceled dispatches release the message-keyed scope before the processor returns an outcome.
- Parallel messages with the same saga correlation retain separate ambient snapshots.
- Each hook instance serializes messages with the same saga correlation before state load.
- Dirty-state conflicts propagate without automatic state merge.
- Completion-only conflicts may retry up to 3 attempts.
- `SetState` and `Complete` in the same dispatch throw `InvalidOperationException` before persisting anything.
- The hook detects state changes by comparing serialized state with the load snapshot. In-place mutations persist without `SetState`. A read-only dispatch saves nothing.
- Only a `LastAppliedMessageId` match suppresses dispatch. New messages on a completed saga dispatch with an inactive scope and no saga mutation.
- `Complete()` with no prior save stores the initial snapshot first, then completes the new row instead of reporting a concurrency conflict.
- Saga save runs after successful handler dispatch but before inbox terminal persistence.
- Inbox terminal state and saga rows are separate persistence steps. The applied-message ledger closes the duplicate-application window without claiming cross-store atomicity.

## Non-Goals

- The hook does not dispatch the next saga step. The application enqueues the next correlated inbox command.
- The hook does not compensate or roll back side effects.
- The hook does not participate in outbox event dispatch.

## Observability

The saga hook does not emit dedicated meters or activities.

Use the following instead:

- Use inbox processor metrics (`litebus.inbox.processor.succeeded`, `failed`, `dead_lettered`) for dispatch outcomes.
- Log dirty-state `SagaConcurrencyException` in the application, including message id and saga definition id.
- When using durable storage, watch PostgreSQL row growth and `QueryAsync` for operational visibility.

## References

- [Architecture](../../architecture/README.md)
- [optimistic-concurrency](optimistic-concurrency.md)
- [store](store.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException`

- **Use case**: The hook rejects simultaneous dirty state and completion in one dispatch.
- **Test kind**: Unit
- **Description**: The test invokes the hook directly after `BeforeDispatchAsync` with both `SetState` and `Complete` on the active scope.
- **Behavior**: The `AfterDispatchAsync` persist guard runs.
- **Expected outcome**: The hook throws `InvalidOperationException`. It writes nothing to the store.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenDispatchOnlyReadsState_SkipsSave`

- **Use case**: A dispatch that reads state without changing it performs no store write.
- **Test kind**: Unit
- **Description**: The handler calls `GetState` and returns the snapshot unchanged.
- **Behavior**: The hook compares the current state with the load snapshot. The two match.
- **Expected outcome**: The hook saves nothing. No row is created for the new saga.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenStateIsReadThenCompleted_CompletesSaga`

- **Use case**: When the handler reads state and then calls `Complete()`, the saga completes.
- **Test kind**: Unit
- **Description**: The handler reads state, changes nothing, and calls `Complete()`.
- **Behavior**: A read carries no changes, so completion is not combined with a state change.
- **Expected outcome**: The saga row completes without a concurrency conflict.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenStateChangesInPlace_PersistsWithoutSetState`

- **Use case**: When the handler mutates the `GetState` instance in place, the change persists without `SetState`.
- **Test kind**: Unit
- **Description**: The handler mutates the returned state object and never calls `SetState`.
- **Behavior**: The serialized state differs from the load snapshot, so the hook saves.
- **Expected outcome**: The stored row carries the mutation at the next version.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenInPlaceChangeCombinesWithComplete_ThrowsInvalidOperationException`

- **Use case**: An in-place change combined with `Complete()` is rejected like `SetState` plus `Complete()`.
- **Test kind**: Unit
- **Description**: The handler mutates state in place and calls `Complete()` in one dispatch.
- **Behavior**: Change detection reports changes, so the persist guard throws.
- **Expected outcome**: The hook throws `InvalidOperationException`. It writes nothing to the store.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Successful inbox dispatch persists handler mutations through the hook after phase.
- **Test kind**: Unit
- **Description**: The test runs an end-to-end inbox processor pass with `SagaProcessorHook` and in-memory stores.
- **Behavior**: The handler increments the saga step through `ISagaContext`.
- **Expected outcome**: The store row shows `Step == 1`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.ProcessPendingAsync_ConcurrentCompletionRequests_ShouldCompleteSagaOnce`

- **Use case**: Completion-only conflicts converge after concurrent inbox dispatch.
- **Test kind**: Unit
- **Description**: Two correlated messages dispatch concurrently and request `ISagaContext.Complete()` against version 1.
- **Behavior**: Message-keyed scopes remain isolated. The stale hook reloads the completed row and resets without a second completion update.
- **Expected outcome**: Both inbox messages complete. The saga row reaches completed version 2 once.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenDirtySaveConflicts_PropagatesAfterSingleAttempt`

- **Use case**: Dirty state cannot be merged safely after a concurrent version advance.
- **Test kind**: Unit
- **Description**: A version-checking store rejects the handler-owned dirty snapshot.
- **Behavior**: The hook performs one save and does not reload or reapply the stale state.
- **Expected outcome**: `SagaConcurrencyException` propagates. The message-keyed scope is released.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenEveryCompletionAttemptConflicts_ThrowsAfterThreeAttempts`

- **Use case**: Completion-only retry is finite under sustained concurrent updates.
- **Test kind**: Unit
- **Description**: Every completion write conflicts after the hook reloads the current active version.
- **Behavior**: The hook stops after 3 completion attempts.
- **Expected outcome**: `SagaConcurrencyException` propagates. The scope is released.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The hook persists to PostgreSQL after in-process inbox dispatch.
- **Test kind**: Integration
- **Description**: The test composes `EnableSaga` with `UsePostgreSqlStorage`.
- **Behavior**: A single correlated command runs through the full hook pipeline.
- **Expected outcome**: The PostgreSQL saga row holds the expected state snapshot.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: The hook loads and saves one saga instance across two correlated inbox messages.
- **Test kind**: Integration
- **Description**: Reserve and capture workflow commands share a correlation id.
- **Behavior**: Two processor passes run. The hook reloads version 1 state before the second dispatch.
- **Expected outcome**: A single physical row remains at version 2 with both workflow flags true.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Handler failure skips the hook save. The compensation message restores the prior saga snapshot.
- **Test kind**: Integration
- **Description**: A failure gate blocks the capture step. A later compensate command runs.
- **Behavior**: Failed capture leaves the saga at version 1. Compensation reloads and saves rollback state.
- **Expected outcome**: The state shows `Compensated == true` with inventory released. The capture inbox row failed.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: The hook uses optimistic concurrency to prevent lost updates under parallel workers.
- **Test kind**: Integration
- **Description**: Two correlated increment messages run across four processors with a delayed handler.
- **Behavior**: Stale dirty state propagates `SagaConcurrencyException` without replacing the winning row.
- **Expected outcome**: State and version advance together. The stale inbox message follows failure policy instead of reporting a lost update as completed.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: The sample composition hook persists saga state across two correlated commands.
- **Test kind**: Composition
- **Description**: The test uses the `AddV6CompositionSmoke` smoke with saga handlers in `LiteBus.Runtime.UnitTests`.
- **Behavior**: Two accepts run through two processor passes.
- **Expected outcome**: The loaded instance shows `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Outbox saga processor hook.
- Automatic next-step dispatch.
- Transactional inbox terminal update and saga save in one database transaction.
