# Saga Processor Hook

- **ID**: `saga.processor-hook`
- **Name**: Saga Processor Hook
- **Maturity**: Extension
- **Summary**: Loads saga state before inbox dispatch and saves or completes it after successful dispatch.

## What It Does

`SagaProcessorHook` implements `IProcessorEnvelopeHook` for saga. On `BeforeDispatchAsync` it resets ambient scope, resolves the saga definition from the message contract, builds a `SagaCorrelation`, loads existing state from `ISagaStore`, and begins an active scope. Completed sagas skip active scope so further correlated messages dispatch without saga mutation.

On `PrepareDispatchScope` and at the start of `AfterDispatchAsync`, the hook attaches the scope identified by `IProcessorEnvelope.MessageId`. The synchronous prepare phase sets `AsyncLocal` state on the same logical flow that calls the dispatcher. Message IDs also prevent concurrent deliveries for one saga correlation from overwriting each other's state snapshots.

On `AfterDispatchAsync`, if the handler marked state dirty, the hook saves once with the observed version and propagates `SagaConcurrencyException` on conflict. It does not reload and replace a newer row with a stale handler-owned snapshot. If the handler called `Complete()`, the hook records completion and may reload and retry up to three attempts because completion is idempotent. Handler-thrown exceptions skip save. The processor then calls `AbandonDispatchScope`, which removes the keyed context entry before retry or dead-letter handling.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaProcessorHook.BeforeDispatchAsync(...)` | `LiteBus.Saga` | Resolve mapping, build correlation, load state, begin scope |
| `SagaProcessorHook.PrepareDispatchScope(...)` | `LiteBus.Saga` | Attach the message-keyed scope before dispatcher execution |
| `SagaProcessorHook.AbandonDispatchScope(...)` | `LiteBus.Saga` | Remove state after canceled or failed dispatch |
| `SagaProcessorHook.AfterDispatchAsync(...)` | `LiteBus.Saga` | Save dirty state once or complete saga with bounded retry |
| Registration via `EnableSaga()` | `LiteBus.Saga.InboxIntegration` | Adds hook as `IProcessorEnvelopeHook` singleton |

## Packages

- `LiteBus.Saga`
- `LiteBus.Orchestration.Abstractions` (implements `IProcessorEnvelopeHook`)
- `LiteBus.Saga.Abstractions` (store and registry dependencies)

## Requires

- `saga.processor-envelope-hooks`
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
- Dirty-state conflicts propagate without automatic state merge.
- Completion-only conflicts may retry up to three attempts.
- `SetState` and `Complete` in the same dispatch throw `InvalidOperationException` before persist.
- Saga save runs after successful handler dispatch but before inbox terminal persistence.
- Inbox terminal state and saga rows are separate persistence steps in v6.

## Non-Goals

- Does not dispatch the next saga step (application enqueues the next correlated inbox command).
- Does not compensate or roll back side effects.
- Does not participate in outbox event dispatch.

## Observability

No dedicated saga hook meters or activity sources in v6.

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
- **Description**: `EnableSaga` + `UsePostgreSqlSagaStorage` composition.
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

#### `LiteBusV6CompositionSmokeTests.AddLiteBusV6_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition hook persists saga state across two correlated commands.
- **Test kind**: Composition
- **Description**: `AddV6CompositionSmoke` smoke with saga handlers in `LiteBus.Runtime.UnitTests`.
- **Behavior**: Two accepts, two processor passes.
- **Expected outcome**: `Step == 2` on loaded instance.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Outbox saga processor hook.
- Automatic next-step dispatch.
- Transactional inbox terminal + saga save in one database transaction (v6).
