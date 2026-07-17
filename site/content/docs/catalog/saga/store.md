# Saga Store Contract

- **ID**: `saga.store`
- **Name**: Saga Store Contract
- **Maturity**: Extension
- **Summary**: Persistence abstraction for load, optimistic save, completion, query, and purge of saga instances.

## What It Does

`ISagaStore` is the durable boundary for saga. `LoadAsync` returns null when no row exists (new saga starts with default state in the hook). `SaveAsync` persists state JSON with expected version for optimistic locking. `CompleteAsync` marks an instance completed without creating phantom rows when version matches.

Operational APIs `QueryAsync` and `PurgeAsync` accept `SagaQueryFilter` and `SagaPurgeFilter` for retention jobs and support tooling. Value objects `SagaSaveItem<TState>`, `SagaCompleteItem`, `SagaInstance<TState>`, and `SagaInstanceSummary` model store inputs and query results.

Custom stores implement `ISagaStore` and expose an `ISagaStorageModule` selected explicitly through `SagaModuleBuilder.RegisterStorage`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `ISagaStore.LoadAsync<TState>(...)` | `LiteBus.Saga.Abstractions` | Read existing instance by correlation, null when absent |
| `ISagaStore.SaveAsync<TState>(...)` | `LiteBus.Saga.Abstractions` | Optimistic write with expected version |
| `ISagaStore.CompleteAsync(...)` | `LiteBus.Saga.Abstractions` | Optimistic completion mark |
| `ISagaStore.QueryAsync(...)` | `LiteBus.Saga.Abstractions` | Operational summaries with filter |
| `ISagaStore.PurgeAsync(...)` | `LiteBus.Saga.Abstractions` | Retention cleanup API |
| `SagaSaveItem`, `SagaCompleteItem`, `SagaInstance`, `SagaInstanceSummary` | `LiteBus.Saga.Abstractions` | Store command and result value objects |

## Packages

- `LiteBus.Saga.Abstractions`

## Requires

- `LiteBus.Messaging.Abstractions` (`IMessageSerializer` for JSON state in shipped stores)

## Invariants

- Save and complete throw `SagaConcurrencyException` when stored version does not match expected version.
- Load of completed instance returns `IsCompleted` true; hook resets scope without activating handlers for mutation.
- Query default `Take` is 100 rows.

## Non-Goals

- EF Core saga storage is not shipped. Use PostgreSQL or implement a custom store.
- Store does not participate in inbox terminal transactions in v6.
- No built-in encryption of saga state JSON (use application-level sensitive field handling or future platform features).

## Observability

No store-level OpenTelemetry instruments in v6.

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

#### `LiteBusV6CompositionSmokeTests.AddLiteBusV6_ShouldPersistSagaStateAcrossCorrelatedCommands`

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
