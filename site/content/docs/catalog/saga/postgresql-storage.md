# PostgreSQL Saga Storage

- **ID**: `saga.postgresql-storage`
- **Name**: PostgreSQL Saga Storage
- **Maturity**: Extension
- **Summary**: PostgreSQL `ISagaStore` with schema bootstrap, tenant-scoped keys, and optimistic locking.

## What It Does

`PostgreSqlSagaStore` persists saga instances in `litebus_saga_instances` by default (configurable schema and table name). Primary key is tenant-scoped: `correlation_id`, `saga_type` (saga definition id), and `tenant_id`. State is JSON in `state_json`; `optimistic_lock_version` supports concurrent updates. `last_applied_message_id` records the most recent inbox message applied to the row so a repeated delivery can be ignored safely.

`UsePostgreSqlStorage(configure)` on `SagaModuleBuilder` selects `PostgreSqlSagaModule` and can wire `NpgsqlDataSource` or connection string helpers through `PostgreSqlSagaModuleBuilder`.

Schema version **1** creates new tables with the applied-message column. The package also ships an additive SQL migration for tables created by older LiteBus releases. Apply `src/LiteBus.Saga.Storage.PostgreSql/Sql/saga/v2/add_last_applied_message_id.sql` before enabling the v6 store against an existing table. `EnsureSchemaCreationOnStartup` defaults to `true` via `PostgreSqlSagaStoreOptions`. Schema bootstrap shares the `PostgreSqlSchemaManager` rail with inbox and outbox when co-located in one database.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaModuleBuilder.UsePostgreSqlStorage(...)` | `LiteBus.Saga.Storage.PostgreSql` | Nested durable saga storage selection |
| `PostgreSqlSagaModuleBuilder` | `LiteBus.Saga.Storage.PostgreSql` | Configures data source, connection string, store options, schema init toggle |
| `PostgreSqlSagaStoreOptions` | `LiteBus.Saga.Storage.PostgreSql` | Configurable schema/table plus startup ensure and validate flags |
| `PostgreSqlSagaStore` | `LiteBus.Saga.Storage.PostgreSql` | PostgreSQL `ISagaStore` implementation |
| `PostgreSqlSagaSchema.EnsureAsync/ValidateAsync` | `LiteBus.Saga.Storage.PostgreSql` | Schema bootstrap and schema drift verification |

## Packages

- `LiteBus.Saga.Storage.PostgreSql`
- `LiteBus.Storage.PostgreSql` (shared schema infrastructure)

## Requires

- `saga.inbox-integration`
- `saga.store`
- PostgreSQL database (often shared with inbox/outbox)
- `Npgsql` data source configuration

## Invariants

- Optimistic updates compare `optimistic_lock_version` on save and complete.
- Save and complete operations update `last_applied_message_id` with the inbox message identifier when the hook supplies one.
- `tenant_id` participates in the primary key. A missing or whitespace tenant identifier is stored as an empty string.
- An omitted `TenantId` query or purge predicate includes every tenant. An explicit whitespace predicate selects unscoped rows.
- Saga schema is separate from inbox message rows; not one transaction with inbox terminal update in v6.

## Non-Goals

- EF Core saga adapter is not shipped.
- Does not run the additive migration automatically. Apply the shipped migration through the application's schema migration process before startup validation.
- Does not encrypt state JSON at rest (see payload encryption on inbox/outbox axis for message payloads).

## Observability

No dedicated PostgreSQL saga meters in v6.

**What to use instead**

- Monitor `litebus_saga_instances` (or configured table) row growth.
- Run `QueryAsync` / `PurgeAsync` for retention and support tooling.
- Coordinate schema startup tasks with inbox/outbox when sharing one database (host manifest startup tasks).

## Deep Docs

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Cookbook and scenarios](../../getting-started/cookbook.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlSagaSchemaTests.EnsureAsync_ShouldCreateSagaSchema`

- **Use case**: Schema ensure creates tables required by `PostgreSqlSagaStore`.
- **Test kind**: Unit
- **Description**: `PostgreSqlSagaSchema.EnsureAsync` then `ValidateAsync` on live container.
- **Behavior**: Embedded v1 DDL applied to unique table name.
- **Expected outcome**: Validate succeeds without exception.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Full composition persists saga row through PostgreSQL store.
- **Test kind**: Integration
- **Description**: `UsePostgreSqlStorage` with shared test data source.
- **Behavior**: Hook save after in-process dispatch.
- **Expected outcome**: Load returns expected state from PostgreSQL.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows`

- **Use case**: Tenant metadata from inbox acceptance reaches the PostgreSQL saga primary key.
- **Test kind**: Integration
- **Description**: Processes two accepts with one correlation identifier under different tenant identifiers.
- **Behavior**: `SagaProcessorHook` builds two tenant-scoped correlations before the store writes state.
- **Expected outcome**: PostgreSQL contains one version 1 saga row per tenant.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.HostedProcessor_StartAsync_ShouldInitializeSagaSchemaBeforeProcessing`

- **Use case**: Host startup must create inbox and saga schemas before the inbox background processor polls.
- **Test kind**: Hosted PostgreSQL integration.
- **Description**: Builds the Microsoft hosting bridge without manually creating either schema and enables the inbox processor.
- **Behavior**: `IHostedService.StartAsync` runs manifest startup tasks, validates the saga schema, and then processes an accepted correlated command.
- **Expected outcome**: Schema validation succeeds and the hosted processor persists saga state at step 1.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: PostgreSQL optimistic version column advances across workflow steps.
- **Test kind**: Integration
- **Description**: Direct table read asserts `optimistic_lock_version == 2`.
- **Behavior**: Two hook-mediated saves.
- **Expected outcome**: Single row; JSON contains expected flags.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: PostgreSQL store retains prior version when handler fails before hook save.
- **Test kind**: Integration
- **Description**: Version stays at 1 after failed capture; compensation bumps to 2.
- **Behavior**: Load/save through hook only on successful dispatch.
- **Expected outcome**: State JSON reflects compensation.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: PostgreSQL row-level optimistic locking under concurrent hook saves.
- **Test kind**: Integration
- **Description**: Table read matches in-memory loaded version after race.
- **Behavior**: Concurrent processors with delayed handler.
- **Expected outcome**: `optimistic_lock_version <= 2`; single row.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreConnectionTests.SaveAndLoad_under_load_should_not_exhaust_connection_pool`

- **Use case**: Direct store usage releases connections under repeated load.
- **Test kind**: Integration
- **Description**: 40 save/load iterations with `MaxPoolSize = 4`.
- **Behavior**: Direct `PostgreSqlSagaStore` API (bypasses hook).
- **Expected outcome**: Counter reaches 40 without pool errors.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.SaveAsync_SameCorrelationAcrossTenants_ShouldKeepRowsIsolated`

- **Use case**: Two tenants can use the same saga definition and correlation identifiers without sharing state.
- **Test kind**: Integration
- **Description**: Saves distinct state under `tenant-a` and `tenant-b`, then loads and queries both rows.
- **Behavior**: The three-column primary key and tenant query predicate are exercised against PostgreSQL.
- **Expected outcome**: Each tenant returns its own state, while an omitted tenant predicate returns both rows.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.QueryAsync_WithFilters_ShouldReturnNewestMatchingRows`

- **Use case**: Operational queries combine definition, correlation, tenant, completion, ordering, and take predicates.
- **Test kind**: Integration
- **Description**: Persists four rows with deterministic timestamps across two tenants and two definitions.
- **Behavior**: Nullable Npgsql parameters use explicit database types, and rows order by `updated_at` descending.
- **Expected outcome**: Exact filters select one completed row; take selects the newest matching row; zero take is rejected.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows`

- **Use case**: Retention removes completed rows before a cutoff without deleting active or newer rows.
- **Test kind**: Integration
- **Description**: Creates old completed rows in two tenants plus a newer completed row and an active row.
- **Behavior**: The first purge is tenant-scoped; the second omits the tenant predicate and includes every tenant.
- **Expected outcome**: Two old completed rows are removed in separate calls; the newer completed and active rows remain.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaStoreOperationsTests.CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion`

- **Use case**: Two workers race to complete the same expected version.
- **Test kind**: Integration
- **Description**: Starts two `CompleteAsync` operations against version 1 of one PostgreSQL row.
- **Behavior**: The update predicate checks version and completion state atomically.
- **Expected outcome**: One call succeeds, one throws `SagaConcurrencyException`, and the stored row reaches completed version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `ValidateAsync` detects schema drift on startup | Yes | Only happy-path validate after ensure | Integration | Low |

### Out-of-Scope Use Cases

- EF Core saga adapter.
- Legacy schema migration beyond shipped v1 scripts.
- Encryption of state JSON at rest.
