# Saga Store Contract

- **ID**: `saga.store`
- **Name**: Saga Store Contract
- **Maturity**: Extension
- **Summary**: Persistence abstraction for load, optimistic save, completion, query, and purge of saga instances.

## What It Does

`ISagaStore` is the durable boundary for saga. `LoadAsync` returns null when no row exists (new saga starts with default state in the hook). `SaveAsync` persists state JSON with expected version for optimistic locking. `CompleteAsync` marks an instance completed without creating phantom rows when version matches; when `Complete()` is the first saga interaction, the hook saves the initial snapshot through `SaveAsync` first and then completes the new row.

Operational APIs `QueryAsync` and `PurgeAsync` accept `SagaQueryFilter` and `SagaPurgeFilter` for retention jobs and support tooling. `SagaPurgeFilter.Take` bounds a purge to at most that many of the oldest matching rows; a purge with no predicate and no `Take` throws `ArgumentException`, and a zero or negative `Take` throws `ArgumentOutOfRangeException`. Value objects `SagaSaveItem<TState>`, `SagaCompleteItem`, `SagaInstance<TState>`, and `SagaInstanceSummary` model store inputs and query results.

Custom stores implement `ISagaStore` and expose an `ISagaStorageModule` selected explicitly through `SagaModuleBuilder.RegisterStorage`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `ISagaStore.LoadAsync<TState>(...)` | `LiteBus.Saga.Abstractions` | Read existing instance by correlation, null when absent |
| `ISagaStore.SaveAsync<TState>(...)` | `LiteBus.Saga.Abstractions` | Optimistic write with expected version |
| `ISagaStore.CompleteAsync(...)` | `LiteBus.Saga.Abstractions` | Optimistic completion mark |
| `ISagaStore.QueryAsync(...)` | `LiteBus.Saga.Abstractions` | Operational summaries with filter |
| `ISagaStore.PurgeAsync(...)` | `LiteBus.Saga.Abstractions` | Retention cleanup API |
| `SagaSaveItem<TState>`, `SagaCompleteItem`, `SagaInstance<TState>`, `SagaInstanceSummary` | `LiteBus.Saga.Abstractions` | Store command and result value objects |

## Packages

- `LiteBus.Saga.Abstractions`

## Requires

- `LiteBus.Messaging.Abstractions` (`IMessageSerializer` for JSON state in shipped stores)

## Invariants

- Save and complete throw `SagaConcurrencyException` when stored version does not match expected version.
- Load of completed instance returns `IsCompleted` true; the hook leaves the scope inactive so the handler still runs but cannot mutate saga state.
- Query default `Take` is 100 rows.
- A save without an applied message id keeps the existing duplicate-dispatch marker instead of erasing it.

## Non-Goals

- EF Core saga storage is not shipped. Use PostgreSQL or implement a custom store.
- The store does not participate in inbox terminal transactions.
- No built-in encryption of saga state JSON (use application-level sensitive field handling or future platform features).

## Observability

The saga store does not emit dedicated OpenTelemetry instruments.

**What to use instead**

- Log `PurgeAsync` return value in retention jobs.
- Application metrics around custom store implementations.
- PostgreSQL table row counts and `QueryAsync` for support dashboards.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [in-memory-store](in-memory-store.md)
- [postgresql-storage](postgresql-storage.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: `LoadAsync` and `SaveAsync` invoked through hook after handler dispatch (in-memory store).
- **Test kind**: Unit
- **Description**: Direct store load after processor pass.
- **Behavior**: First save from hook at version 0.
- **Expected outcome**: Instance with `Step == 1`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaStoreConnectionTests.SaveAndLoad_under_load_should_not_exhaust_connection_pool`

- **Use case**: Repeated `LoadAsync`/`SaveAsync` cycles do not exhaust connection pool.
- **Test kind**: Integration
- **Description**: Direct `PostgreSqlSagaStore` with `MaxPoolSize = 4`; 40 iterations.
- **Behavior**: Optimistic save with reload each iteration.
- **Expected outcome**: Final counter 40; no pool exhaustion.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.QueryAsync_WithFilters_ShouldReturnNewestMatchingRows`

- **Use case**: The PostgreSQL implementation satisfies the complete operational query contract.
- **Test kind**: Integration
- **Description**: Combines all optional predicates with deterministic timestamps and take limits.
- **Behavior**: Definition, correlation, tenant, and completion predicates execute with typed nullable parameters.
- **Expected outcome**: Results are filtered and ordered by update time descending; invalid take values fail before SQL execution.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows`

- **Use case**: The PostgreSQL implementation satisfies tenant-scoped and all-tenant retention behavior.
- **Test kind**: Integration
- **Description**: Purges old completed rows across two calls while retaining active and newer rows.
- **Behavior**: `CompletedBefore` implies completed state and uses the row's last update timestamp.
- **Expected outcome**: Each call returns the exact deleted-row count, and active state remains loadable.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `SagaPurgeBoundTests.PurgeAsync_WithEmptyFilter_ThrowsArgumentException`

- **Use case**: An unscoped, unbounded purge is refused instead of deleting every saga row.
- **Test kind**: Unit
- **Description**: `PurgeAsync` with a default filter against the in-memory store.
- **Behavior**: The store rejects a filter with no predicates and no `Take`.
- **Expected outcome**: `ArgumentException`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPurgeBoundTests.PurgeAsync_WithNonPositiveTake_ThrowsArgumentOutOfRange`

- **Use case**: A zero or negative `Take` limit is rejected before any delete.
- **Test kind**: Unit
- **Description**: `PurgeAsync` with `Take` 0 and -1 against the in-memory store.
- **Behavior**: The store validates the limit before building the delete.
- **Expected outcome**: `ArgumentOutOfRangeException` for both values.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPurgeBoundTests.PurgeAsync_WithTake_DeletesOldestMatchesFirst`

- **Use case**: A `Take`-only purge removes at most `Take` rows, oldest first.
- **Test kind**: Unit
- **Description**: Fifteen rows with deterministic timestamps, then a `Take` 10 purge.
- **Behavior**: The store orders matches by update time ascending and deletes the first ten.
- **Expected outcome**: Ten rows removed; the five newest rows remain.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPurgeBoundTests.PurgeAsync_WithCompletedBeforeAndNoTake_DeletesEveryMatch`

- **Use case**: A predicate-only purge still deletes every match.
- **Test kind**: Unit
- **Description**: Three old completed rows, one recent completed row, and one active row.
- **Behavior**: `CompletedBefore` without `Take` selects all matching rows.
- **Expected outcome**: Three rows removed; the recent completed and active rows remain.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Store versioning advances across two saves for one correlation.
- **Test kind**: Integration
- **Description**: End-to-end via hook; direct `LoadAsync` assertions.
- **Behavior**: Version 0 insert then version 1 update.
- **Expected outcome**: `Version == 2`; single row in table.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: `SaveAsync` optimistic conflicts under concurrent writers.
- **Test kind**: Integration
- **Description**: Parallel processors racing on same correlation.
- **Behavior**: Hook-mediated saves with version checks.
- **Expected outcome**: Final version in `[1, 2]`.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Explicit in-memory store satisfies the contract through composition.
- **Test kind**: Composition
- **Description**: `ISagaStore.LoadAsync` after two correlated dispatches.
- **Behavior**: Two sequential saves via hook.
- **Expected outcome**: `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- EF Core saga storage adapter.
- Transactional inbox plus saga commit in one transaction.
- Custom store implementations (Cosmos DB, SQL Server, and similar) beyond contract documentation.
