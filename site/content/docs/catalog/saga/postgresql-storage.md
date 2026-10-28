# PostgreSQL Saga Storage

- **ID**: `saga.postgresql-storage`
- **Name**: PostgreSQL Saga Storage
- **Maturity**: Extension
- **Summary**: A PostgreSQL `ISagaStore` with schema bootstrap, tenant-scoped keys, and optimistic locking.

## What It Does

`PostgreSqlSagaStore` persists saga instances in `litebus_saga_instances` by default (configurable schema and table name). The primary key is tenant-scoped: `correlation_id`, `saga_type` (saga definition id), and `tenant_id`. State is JSON in `state_json`. `optimistic_lock_version` holds the version that concurrent updates compare. `last_applied_message_id` records the most recent inbox message applied to the row so a repeated delivery can be ignored safely.

`UsePostgreSqlStorage(configure)` on `SagaModuleBuilder` selects `PostgreSqlSagaModule`. It can wire `NpgsqlDataSource` or connection string helpers through `PostgreSqlSagaModuleBuilder`.

Schema version **1** defines the current saga table, including the applied-message column. Schema bootstrap shares the `PostgreSqlSchemaManager` validation path with inbox and outbox when they use one database.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaModuleBuilder.UsePostgreSqlStorage(...)` | `LiteBus.Saga.Storage.PostgreSql` | The nested durable saga storage selection |
| `PostgreSqlSagaModuleBuilder` | `LiteBus.Saga.Storage.PostgreSql` | Configures the data source, connection string, store options, and schema init toggle |
| `PostgreSqlSagaStoreOptions` | `LiteBus.Saga.Storage.PostgreSql` | Configurable schema and table plus startup ensure and validate flags |
| `PostgreSqlSagaStore` | `LiteBus.Saga.Storage.PostgreSql` | The PostgreSQL `ISagaStore` implementation |
| `PostgreSqlSagaSchema.EnsureAsync/ValidateAsync` | `LiteBus.Saga.Storage.PostgreSql` | Schema bootstrap and schema drift verification |

## Packages

- `LiteBus.Saga.Storage.PostgreSql`
- `LiteBus.Storage.PostgreSql` (shared schema infrastructure)

## Requires

- `saga.inbox-integration`
- `saga.store`
- A PostgreSQL database (often shared with inbox and outbox).
- An `Npgsql` data source configuration.

## Invariants

- Optimistic updates compare `optimistic_lock_version` on save and complete.
- When the hook supplies one, save and complete operations update `last_applied_message_id` with the inbox message identifier.
- `tenant_id` participates in the primary key. A missing or whitespace tenant identifier is stored as an empty string.
- An omitted `TenantId` query or purge predicate includes every tenant. An explicit whitespace predicate selects unscoped rows.
- A purge with no predicate and no `Take` throws `ArgumentException`. A set `Take` deletes at most that many of the oldest matches.
- The saga schema is separate from inbox message rows. Saga persistence and the inbox terminal update do not share a transaction.

## Non-Goals

- LiteBus does not ship an EF Core saga adapter.
- The store does not encrypt state JSON at rest (see payload encryption on the inbox and outbox axis for message payloads).

## Observability

The PostgreSQL saga store does not emit dedicated meters.

Use the following instead:

- Monitor `litebus_saga_instances` (or configured table) row growth.
- Run `QueryAsync` and `PurgeAsync` for retention and support tooling.
- Coordinate schema startup tasks with inbox and outbox when sharing one database (host manifest startup tasks).

## References

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Cookbook and scenarios](../../getting-started/cookbook.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlSagaSchemaTests.EnsureAsync_ShouldCreateSagaTableReadableThroughTableReaders`

- **Use case**: Schema ensure creates tables required by `PostgreSqlSagaStore`.
- **Test kind**: Integration
- **Description**: `PostgreSqlSagaSchema.EnsureAsync` runs on a live container. The test then reads the created table through `PostgreSqlTableReaders`. A second case asserts that `ValidateAsync` fails on a drifted schema.
- **Behavior**: The test applies embedded v1 DDL to a unique table name.
- **Expected outcome**: The table has the expected columns. A drifted schema fails validation.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The full composition persists the saga row through the PostgreSQL store.
- **Test kind**: Integration
- **Description**: The test uses `UsePostgreSqlStorage` with a shared test data source.
- **Behavior**: The hook saves after in-process dispatch.
- **Expected outcome**: A load returns the expected state from PostgreSQL.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows`

- **Use case**: Tenant metadata from inbox acceptance reaches the PostgreSQL saga primary key.
- **Test kind**: Integration
- **Description**: The test processes two accepts with one correlation identifier under different tenant identifiers.
- **Behavior**: `SagaProcessorHook` builds two tenant-scoped correlations before the store writes state.
- **Expected outcome**: PostgreSQL contains one version 1 saga row per tenant.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.HostedProcessor_StartAsync_ShouldInitializeSagaSchemaBeforeProcessing`

- **Use case**: Host startup must create inbox and saga schemas before the inbox background processor polls.
- **Test kind**: Hosted PostgreSQL integration.
- **Description**: The test builds the Microsoft hosting bridge without manually creating either schema. It then enables the inbox processor.
- **Behavior**: `IHostedService.StartAsync` runs manifest startup tasks, validates the saga schema, and then processes an accepted correlated command.
- **Expected outcome**: Schema validation succeeds. The hosted processor persists saga state at step 1.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: The PostgreSQL optimistic version column advances across workflow steps.
- **Test kind**: Integration
- **Description**: A direct table read asserts `optimistic_lock_version == 2`.
- **Behavior**: The hook saves twice.
- **Expected outcome**: A single row remains. The JSON contains the expected flags.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: When the handler fails before the hook save, the PostgreSQL store retains the prior version.
- **Test kind**: Integration
- **Description**: The version stays at 1 after failed capture. Compensation bumps it to 2.
- **Behavior**: Loads and saves run through the hook only on successful dispatch.
- **Expected outcome**: The state JSON reflects compensation.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: PostgreSQL applies row-level optimistic locking under concurrent hook saves.
- **Test kind**: Integration
- **Description**: The table read matches the in-memory loaded version after the race.
- **Behavior**: Concurrent processors run with a delayed handler.
- **Expected outcome**: The table shows `optimistic_lock_version <= 2` with a single row.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreConnectionTests.SaveAndLoad_under_load_should_not_exhaust_connection_pool`

- **Use case**: Direct store usage releases connections under repeated load.
- **Test kind**: Integration
- **Description**: The test runs 40 save and load iterations with `MaxPoolSize = 4`.
- **Behavior**: The test uses the direct `PostgreSqlSagaStore` API, which bypasses the hook.
- **Expected outcome**: The counter reaches 40 without pool errors.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.SaveAsync_SameCorrelationAcrossTenants_ShouldKeepRowsIsolated`

- **Use case**: Two tenants can use the same saga definition and correlation identifiers without sharing state.
- **Test kind**: Integration
- **Description**: The test saves distinct state under `tenant-a` and `tenant-b`, then loads and queries both rows.
- **Behavior**: The test exercises the three-column primary key and the tenant query predicate against PostgreSQL.
- **Expected outcome**: Each tenant returns its own state. An omitted tenant predicate returns both rows.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.QueryAsync_WithFilters_ShouldReturnNewestMatchingRows`

- **Use case**: Operational queries combine definition, correlation, tenant, completion, ordering, and take predicates.
- **Test kind**: Integration
- **Description**: The test persists four rows with deterministic timestamps across two tenants and two definitions.
- **Behavior**: Nullable Npgsql parameters use explicit database types. Rows order by `updated_at` descending.
- **Expected outcome**: Exact filters select one completed row. Take selects the newest matching row. Zero take is rejected.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows`

- **Use case**: Retention removes completed rows before a cutoff without deleting active or newer rows.
- **Test kind**: Integration
- **Description**: The test creates old completed rows in two tenants plus a newer completed row and an active row.
- **Behavior**: The first purge is tenant-scoped. The second omits the tenant predicate and includes every tenant.
- **Expected outcome**: Two old completed rows are removed in separate calls. The newer completed and active rows remain.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion`

- **Use case**: Two workers race to complete the same expected version.
- **Test kind**: Integration
- **Description**: The test starts two `CompleteAsync` operations against version 1 of one PostgreSQL row.
- **Behavior**: The update predicate checks version and completion state atomically.
- **Expected outcome**: One call succeeds. One throws `SagaConcurrencyException`. The stored row reaches completed version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaPurgeBoundTests.PurgeAsync_WithEmptyFilter_ThrowsArgumentException`

- **Use case**: An unscoped, unbounded purge is refused instead of deleting every saga row.
- **Test kind**: Integration
- **Description**: The test calls `PurgeAsync` with a default filter against PostgreSQL.
- **Behavior**: The store rejects a filter with no predicates and no `Take`.
- **Expected outcome**: The store throws `ArgumentException`.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaPurgeBoundTests.PurgeAsync_WithNonPositiveTake_ThrowsArgumentOutOfRange`

- **Use case**: A zero or negative `Take` limit is rejected before any delete.
- **Test kind**: Integration
- **Description**: The test calls `PurgeAsync` with `Take` 0 and -1 against PostgreSQL.
- **Behavior**: The store validates the limit before building the delete.
- **Expected outcome**: The store throws `ArgumentOutOfRangeException` for both values.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaPurgeBoundTests.PurgeAsync_WithTake_DeletesOldestMatchesFirst`

- **Use case**: A `Take`-only purge removes at most `Take` rows, oldest first.
- **Test kind**: Integration
- **Description**: The test creates twelve rows with deterministic timestamps, then runs a `Take` 10 purge.
- **Behavior**: The store orders matches by update time ascending. It deletes the first ten.
- **Expected outcome**: Ten rows are removed. The two newest rows remain.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `ValidateAsync` detects schema drift on startup | Yes | Only the happy-path validate after ensure is tested | Integration | Low |

### Out-of-Scope Use Cases

- EF Core saga adapter.
- Migration of incompatible existing saga tables.
- Encryption of state JSON at rest.
