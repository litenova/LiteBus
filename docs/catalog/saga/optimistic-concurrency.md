# Optimistic Concurrency

- **ID**: `saga.optimistic-concurrency`
- **Name**: Optimistic Concurrency
- **Maturity**: Extension
- **Summary**: Versioned saga saves with conflict detection, safe completion retry, and explicit concurrency exceptions.

## What It Does

Every saga instance carries an optimistic lock version observed on load. `ISagaStore.SaveAsync` and `CompleteAsync` require `ExpectedVersion` on `SagaSaveItem` and `SagaCompleteItem`. Mismatch throws `SagaConcurrencyException` with the failing `SagaCorrelation`.

After successful handler dispatch, `SagaProcessorHook` saves dirty state once with the version observed before dispatch. A conflict propagates immediately because the hook cannot infer how to merge an arbitrary handler-owned state snapshot with a concurrent update. Replacing the newer row with the stale snapshot would report success while losing state.

Completion-only conflicts are different because completion is idempotent. The hook reloads the current version and retries completion up to three attempts. If another worker already completed the saga, the hook treats the requested completion as satisfied.

Raising inbox `DispatcherConcurrency` above one increases concurrent updates to the same correlation. Active state is isolated by durable message ID, so concurrent handlers cannot overwrite one another inside the singleton execution context. Persistence still resolves conflicts through expected versions. Handlers must stay idempotent and expect dirty conflicts to follow inbox failure policy or later redelivery.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaConcurrencyException` | `LiteBus.Saga.Abstractions` | Signals version mismatch during save or complete |
| `SagaSaveItem<TState>.ExpectedVersion` | `LiteBus.Saga.Abstractions` | Optimistic version used for save operation |
| `SagaCompleteItem.ExpectedVersion` | `LiteBus.Saga.Abstractions` | Optimistic version used for completion operation |
| `SagaProcessorHook.AfterDispatchAsync(...)` | `LiteBus.Saga` | Propagates dirty conflicts and retries completion-only conflicts up to 3 attempts |
| `InMemorySagaStore` and `PostgreSqlSagaStore` | `LiteBus.Saga`, `LiteBus.Saga.Storage.PostgreSql` | Concrete version-checking implementations |

## Packages

- `LiteBus.Saga.Abstractions`
- `LiteBus.Saga`
- `LiteBus.Saga.Storage.PostgreSql` (durable version column)

## Requires

- `saga.processor-hook`
- `saga.store` (version-aware implementation)

## Invariants

- Handler failure before `AfterDispatchAsync` does not increment stored version for that attempt.
- Insert uses expected version 0 on first save from hook after new saga scope.
- Missing rows reject nonzero expected versions, and completed rows reject save or repeated completion.
- In-memory completion uses an atomic compare-and-update operation.
- Dirty-state conflicts propagate after one failed save attempt.
- Completion-only conflicts reload and retry up to three attempts, or succeed when the row is already completed.

## Non-Goals

- No distributed lock or lease on saga rows.
- No automatic merge of conflicting handler mutations.
- No saga-specific dead-letter reason separate from inbox hook failure semantics.

## Observability

No concurrency-specific meters in v6.

**What to use instead**

- Log dirty-state `SagaConcurrencyException` with message id and saga definition id.
- Monitor inbox failed/dead-letter counts when dirty saga writes conflict.
- Compare `optimistic_lock_version` in PostgreSQL support queries after parallel worker incidents.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [store](store.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: Concurrent workers racing on same correlation produce bounded version increment under optimistic locking.
- **Test kind**: Integration
- **Description**: Four parallel processors, two increment messages, handler delay gate.
- **Behavior**: A stale dirty save propagates `SagaConcurrencyException`; a worker that loaded after the first save may advance the next version normally.
- **Expected outcome**: Final state and version advance together; no message reports completion after losing its increment.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Sequential saves advance version monotonically without conflict.
- **Test kind**: Integration
- **Description**: Two-step workflow on one correlation.
- **Behavior**: Save at version 0 then version 1.
- **Expected outcome**: Final version 2; table column matches loaded instance.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreConnectionTests.SaveAndLoad_under_load_should_not_exhaust_connection_pool`

- **Use case**: Repeated optimistic saves succeed under sequential load without version conflicts.
- **Test kind**: Integration
- **Description**: 40 sequential load/save cycles with incrementing expected version.
- **Behavior**: Direct store API optimistic updates.
- **Expected outcome**: Counter 40; no `SagaConcurrencyException`.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `SagaProcessorHookTests.ParallelDispatches_WithSameCorrelation_IsolateScopeByMessageId`

- **Use case**: Two simultaneous messages for one saga retain separate active snapshots before versioned persistence.
- **Test kind**: Unit
- **Description**: Both flows attach to one singleton `SagaExecutionContext`, mutate different state values, and persist concurrently.
- **Behavior**: One first insert succeeds; the conflicting stale flow propagates `SagaConcurrencyException` without overwriting the winner.
- **Expected outcome**: Both flows retain independent state before save; one persists version 1 and one reports a conflict.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `InMemorySagaStoreTests`

- **Use case**: In-memory save and completion enforce the same expected-version contract as PostgreSQL.
- **Test kind**: Unit
- **Description**: Stale saves, nonzero first inserts, completed-row saves, missing completion, repeated completion, and concurrent completion are forced directly.
- **Behavior**: The store checks the existing immutable row and updates it atomically.
- **Expected outcome**: Exactly one concurrent completion succeeds; every stale operation throws `SagaConcurrencyException`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaStoreOperationsTests.CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion`

- **Use case**: PostgreSQL completion uses one atomic version-checked update under concurrent workers.
- **Test kind**: Integration
- **Description**: Two direct store calls attempt to complete version 1 of the same row.
- **Behavior**: One update wins and the stale update reports `SagaConcurrencyException`.
- **Expected outcome**: One success, one conflict, and one completed row at version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `SagaProcessorHookTests.ProcessPendingAsync_ConcurrentCompletionRequests_ShouldCompleteSagaOnce`

- **Use case**: Two concurrent inbox dispatches request completion for the same loaded saga version.
- **Test kind**: Unit
- **Description**: A gated dispatcher calls `ISagaContext.Complete()` from two message-scoped flows with dispatcher concurrency 2.
- **Behavior**: The winning hook completes version 1; the losing hook reloads the completed row and treats completion as already satisfied.
- **Expected outcome**: Both inbox messages complete, and the saga row is completed once at version 2.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenCompletionConflictsWithActiveSaga_RetriesCurrentVersion`

- **Use case**: A completion request conflicts with a concurrent nonterminal saga update.
- **Test kind**: Unit
- **Description**: The store rejects the observed version, then exposes the newer active version.
- **Behavior**: The hook reloads and retries completion against the current version.
- **Expected outcome**: Completion succeeds on the second attempt without replacing saga state.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatchAsync_WhenEveryCompletionAttemptConflicts_ThrowsAfterThreeAttempts`

- **Use case**: Completion retry remains bounded under continuous concurrent writes.
- **Test kind**: Unit
- **Description**: Every version-checked completion attempt reports a conflict.
- **Behavior**: The hook reloads between attempts and stops after the third completion write.
- **Expected outcome**: `SagaConcurrencyException` propagates after three attempts.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

### Out-of-Scope Use Cases

- Distributed lock or lease on saga rows.
- Automatic merge of conflicting handler-owned state.
- Saga-specific dead-letter reason codes separate from inbox hook failure policy.
