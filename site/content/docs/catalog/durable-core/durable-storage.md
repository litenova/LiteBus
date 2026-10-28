# Durable Storage

**ID:** `durable-core.durable-storage`
**Maturity:** GA

## Summary

Persist inbox and outbox envelopes through role-split store interfaces with PostgreSQL, EF Core, and in-memory implementations.

## What It Does

Storage adapters implement narrow roles: append (writer), lease (processor), state persist (processor), dead letter, retention, diagnostics, query, and purge. A single class typically implements all roles on one table set. PostgreSQL inbox and saga components use schema version 1; the PostgreSQL outbox component uses schema version 2, which adds the nullable `handler` column for per-handler delivery, and `EnsureAsync` upgrades a version 1 table automatically. EF Core schemas are owned by the application. Shared PostgreSQL primitives live in `LiteBus.Storage.PostgreSql`.

## Public Surface

### Store Role Interfaces

| Role | Inbox interface | Outbox interface |
| --- | --- | --- |
| Append (writer) | `IInboxStore` | `IOutboxStore` |
| Lease (processor) | `IInboxLeaseStore` | `IOutboxLeaseStore` |
| Terminal persist | `IInboxStateWriter` | `IOutboxStateWriter` |
| Dead letter ops | `IInboxDeadLetterStore` | `IOutboxDeadLetterStore` |
| Retention | `IInboxRetentionStore` | `IOutboxRetentionStore` |
| Diagnostics | `IInboxDiagnosticsStore` | `IOutboxDiagnosticsStore` |

Query and purge roles are exposed through manager APIs backed by the same store implementations.

### Module Registration (Compose-Time)

| Extension | Package | Backend |
| --- | --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory`, `LiteBus.Outbox.Storage.InMemory` | Process-local, single-worker |
| `UsePostgreSqlStorage(configure)` | `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.PostgreSql` | PostgreSQL schema v1 (inbox), v2 (outbox) |
| `UseEntityFrameworkCoreStorage(configure)` | `LiteBus.Inbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.EntityFrameworkCore` | Application-owned EF migrations |
| `UseDataSource(NpgsqlDataSource)` | `LiteBus.Storage.PostgreSql` | Shared connection pool across axes |
| `EnsureSchemaCreationOnStartup()` | PostgreSQL storage modules | Dev-only schema bootstrap via startup task |

Register storage inside **`AddInbox(...)`** / **`AddOutbox(...)`** builders only. Calling the same storage extension twice on one builder throws **`DurableStorageConfigurationException`**.

### Configuration

- PostgreSQL: connection string or shared **`NpgsqlDataSource`**, schema name, table prefix options on module builders
- EF Core: **`DbContext`** type, schema ownership, transactional interceptor pairing (see transactional-writes capability)
- In-memory: no external configuration; process-wide lock (not multi-worker)

### Runtime Dependencies

- **`IMessageContractRegistry`**: contract name and version before append
- **`IInboxEnvelopeFactory`** / **`IOutboxEnvelopeFactory`**: serialization at writer boundary
- One data source per database when sharing PostgreSQL between inbox and outbox modules

### Extension Points

- Custom stores implement role interfaces and register through storage module pattern (see [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md))
- Shared PostgreSQL infrastructure: **`LiteBus.Storage.PostgreSql`** for ambient transactions and NOTIFY work signals

## Store Roles

| Role | Inbox | Outbox |
| --- | --- | --- |
| Append | `IInboxStore` | `IOutboxStore` |
| Lease | `IInboxLeaseStore` | `IOutboxLeaseStore` |
| Terminal persist | `IInboxStateWriter` | `IOutboxStateWriter` |
| Dead letter ops | `IInboxDeadLetterStore` | `IOutboxDeadLetterStore` |
| Retention | `IInboxRetentionStore` | `IOutboxRetentionStore` |
| Diagnostics | `IInboxDiagnosticsStore` | `IOutboxDiagnosticsStore` |

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Storage.PostgreSql` | Shared Npgsql helpers, ambient transaction provider |
| `LiteBus.Outbox.Storage.Marten` | Queued transactional outbox writer on the current Marten session |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Axis-specific stores |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Store interface definitions |

## Requires

- Storage registered inside `AddInbox` / `AddOutbox` builder
- Contract registration before writes
- One data source per database when sharing PostgreSQL between axes

## Invariants

- Default table: `litebus_inbox_messages` / outbox equivalent with PK `message_id`
- In-memory store uses process-wide lock; not for multi-worker production simulation
- Custom stores implement roles; use envelope factories for serialization
- PostgreSQL inbox and saga components use schema version 1; the outbox component uses schema version 2 with automatic upgrade from version 1 under `EnsureAsync`

## Non-Goals

- Dedicated SQL Server store (the EF Core adapter supports the SQL Server provider; a dedicated adapter is planned on [Roadmap](../../roadmap/README.md))
- SQLite production store
- Shared storage implementation across adapters; each adapter owns its provider-specific behavior

## Observability

### Schema Diagnostic Probes

- **Contract**: **`IDiagnosticCheck`** registered through storage modules
- **Kind**: Framework-neutral diagnostic probe (platform contract)
- **When run**: Host startup and manual diagnostic runner invocations
- **Signal**: Healthy when required tables and columns exist; degraded or unhealthy on drift
- **Registration**: `configuration.RegisterDiagnosticCheck(typeof(...), name)` inside storage module **`Build()`**
- **Operational note**: Wire to ASP.NET health checks through hosting axis; LiteBus ships probes, not HTTP endpoints

### Schema Info API

- **Member**: **`GetSchemaInfoAsync`** on **`IInboxDiagnosticsStore`** / **`IOutboxDiagnosticsStore`**
- **Kind**: Management query (no dedicated meter)
- **When called**: Operator tooling and management endpoints
- **Returns**: **`StoreSchemaInfo`** with version and table metadata
- **Operational note**: Use before deploy to confirm the current schema version, column types, and indexes

### Queue Depth Gauges

- **Instrument**: `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth`
- **Constants**: `LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`
- **Kind**: Observable gauge
- **Tags**: `litebus.inbox.status` / `litebus.outbox.status`
- **Source**: Diagnostics store role reads status counts during metric scrape
- **Registration**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`

### PostgreSQL NOTIFY Work Signal

- **Kind**: Internal wake mechanism (not a public meter)
- **When emitted**: Row insert triggers NOTIFY; processor may wake before poll timeout
- **Operational note**: Reduces latency after accept; poll interval still applies as fallback

### Retention Cleanup Errors

- **Instrument**: `litebus.inbox.cleanup.errors` / `litebus.outbox.cleanup.errors`
- **Constants**: `LiteBusInboxTelemetry.CleanupErrorInstrumentName`, `LiteBusOutboxTelemetry.CleanupErrorInstrumentName`
- **Kind**: Counter
- **When incremented**: Retention background service purge failure

## Deep Docs

- [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md)
- [PostgreSQL schema management](../../integrations/postgresql-schema-management.md)
- [Inbox EF Core storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Storage.UnitTests` (`InMemory/`, `EntityFrameworkCore/`)
- `LiteBus.Storage.IntegrationTests` (`PostgreSql/`, `EntityFrameworkCore/`)
- `LiteBus.Storage.Testing` (shared inbox/outbox store contract suites)

### Covered Use Cases

#### `InboxStoreContractTests` (All `[Fact]` Methods)

- **Use case**: When inbox store roles are exercised, append, lease, persist, query, and purge behavior matches across backends
- **Test kind**: Contract
- **Description**: Full contract suite run per in-memory and adapter implementation
- **Behavior**: All `[Fact]` methods in **`InboxStoreContractTests`**
- **Expected outcome**: Parity across in-memory implementations and production adapters
- **Remarks**: Run per adapter project

#### `OutboxStoreContractTests` (All `[Fact]` Methods)

- **Use case**: When outbox store roles are exercised, behavior matches across backends
- **Test kind**: Contract
- **Description**: Full contract suite for outbox roles
- **Behavior**: All `[Fact]` methods in **`OutboxStoreContractTests`**
- **Expected outcome**: Parity across backends
- **Remarks**: Run per adapter project

#### `InboxRetentionStoreContractTests` (All Methods)

- **Use case**: When retention runs on completed inbox rows, eligible rows are deleted per cutoff semantics
- **Test kind**: Contract
- **Description**: Retention role contract suite
- **Behavior**: **`DeleteCompletedOlderThanAsync`** and related retention methods
- **Expected outcome**: Eligible rows deleted; recent rows retained
- **Remarks**: Store contract suite

#### `OutboxRetentionStoreContractTests` (All Methods)

- **Use case**: When retention runs on published outbox rows, eligible rows are purged
- **Test kind**: Contract
- **Description**: Outbox retention role contract suite
- **Behavior**: **`DeletePublishedOlderThanAsync`** and related methods
- **Expected outcome**: Old published rows removed
- **Remarks**: Store contract suite

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: When in-memory inbox storage is registered, all store role interfaces resolve from DI
- **Test kind**: Unit
- **Description**: Module registration through inbox builder
- **Behavior**: **`UseInMemoryStorage()`** on inbox module
- **Expected outcome**: All store interfaces registered
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Inbox/`)

#### `InMemoryOutboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: When in-memory outbox storage is registered, all store role interfaces resolve from DI
- **Test kind**: Unit
- **Description**: Module registration through outbox builder
- **Behavior**: **`UseInMemoryStorage()`** on outbox module
- **Expected outcome**: All store interfaces registered
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Outbox/`)

#### `PostgreSqlInboxSchemaTests.EnsureAsync_ShouldCreateSchemaAndBeIdempotent`

- **Use case**: When PostgreSQL inbox schema ensure runs, tables are created and repeat calls are idempotent
- **Test kind**: Integration
- **Description**: Schema bootstrap for inbox tables
- **Behavior**: **`EnsureAsync`** twice
- **Expected outcome**: Tables created once; second call succeeds without error
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlOutboxSchemaTests.EnsureAsync_ShouldCreateSchemaAndBeIdempotent`

- **Use case**: When PostgreSQL outbox schema ensure runs, DDL is idempotent
- **Test kind**: Integration
- **Description**: Schema bootstrap for outbox tables
- **Behavior**: **`EnsureAsync`** twice
- **Expected outcome**: Idempotent DDL
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaDriftTests.InboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns`

- **Use case**: When inbox schema validation detects missing columns, the error lists required columns
- **Test kind**: Integration
- **Description**: Schema drift detection for inbox
- **Behavior**: **`ValidateAsync`** against incomplete schema
- **Expected outcome**: Actionable drift error with missing column names
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaDriftTests.OutboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns`

- **Use case**: When outbox schema validation detects missing columns, the error lists required columns
- **Test kind**: Integration
- **Description**: Schema drift detection for outbox
- **Behavior**: **`ValidateAsync`** against incomplete schema
- **Expected outcome**: Missing columns reported
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaHostingTests.InboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup`

- **Use case**: When schema creation on startup is enabled, the hosted startup task creates inbox schema
- **Test kind**: Integration
- **Description**: **`EnsureSchemaCreationOnStartup()`** startup task
- **Behavior**: Host run with schema initializer enabled
- **Expected outcome**: Schema present after startup
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: When EF Core inbox model is configured, v1 column mapping matches expected schema
- **Test kind**: Unit
- **Description**: EF model builder configuration
- **Behavior**: **`GetModelBuilderConfiguration`**
- **Expected outcome**: Expected v1 columns mapped
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Inbox/`)

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: When EF Core outbox model is configured, v1 column mapping matches expected schema
- **Test kind**: Unit
- **Description**: EF model builder configuration
- **Behavior**: **`GetModelBuilderConfiguration`**
- **Expected outcome**: Expected v1 columns mapped
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles`

- **Use case**: When EF outbox storage registers, one store instance satisfies all role interfaces
- **Test kind**: Unit
- **Description**: DI registration for EF outbox store
- **Behavior**: Module build and service resolution
- **Expected outcome**: Single instance registered for writer, lease, and state roles
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `PostgreSqlModuleRegistrationTests.AddPostgreSqlInboxStorage_ShouldRegisterWriterLeaseAndStateRoles`

- **Use case**: When PostgreSQL inbox storage module builds, writer, lease, and state roles register
- **Test kind**: Integration
- **Description**: PostgreSQL module registration
- **Behavior**: **`UsePostgreSqlStorage`** on inbox builder
- **Expected outcome**: Writer + lease + state interfaces available
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlModuleRegistrationTests.SharedDataSource_inbox_and_outbox_modules_should_resolve_same_instance`

- **Use case**: When inbox and outbox modules share a data source, they resolve the same pool instance
- **Test kind**: Integration
- **Description**: **`UseDataSource(NpgsqlDataSource)`** shared configuration
- **Behavior**: Both modules register with same data source
- **Expected outcome**: Same singleton pool instance
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlInboxWorkSignalNotifyTests.WaitForWorkOrDelayAsync_when_row_inserted_should_wake_before_poll_timeout`

- **Use case**: When a row is inserted, PostgreSQL NOTIFY wakes the processor before the poll timeout elapses
- **Test kind**: Integration
- **Description**: Work signal NOTIFY path
- **Behavior**: Insert row while processor waits
- **Expected outcome**: Early wake before poll timeout
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxStoreParityTests.LeasePendingAsync_FiltersByTenantId`

- **Use case**: When EF outbox lease runs with tenant filter, only matching tenant rows are leased
- **Test kind**: Unit
- **Description**: Tenant filter in EF lease query
- **Behavior**: **`LeasePendingAsync`** with tenant-scoped processor options
- **Expected outcome**: Scoped rows only in lease batch
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_WhenCalledTwiceOnBuilder_ShouldThrow`

- **Use case**: When in-memory storage is registered twice on the same builder, configuration fails at compose time
- **Test kind**: Unit
- **Description**: Builder guard against duplicate storage registration
- **Behavior**: Two **`UseInMemoryStorage()`** calls on one inbox builder
- **Expected outcome**: **`LiteBusConfigurationException`** reporting that inbox storage is already configured
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Inbox/`); the builder throws the derived `DurableStorageConfigurationException`

#### `PostgreSqlOutboxSchemaUpgradeTests.EnsureAsync_WhenVersion1TableExists_ShouldUpgradeToVersion2AndPreserveRows`

- **Use case**: When a version 1 outbox table exists, schema ensure upgrades it to version 2 without losing rows
- **Test kind**: Integration
- **Description**: Existing version 1 table with stored envelopes
- **Behavior**: **`EnsureAsync`** against the old table shape
- **Expected outcome**: Nullable `handler` column added; existing rows preserved
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`); companion tests cover fresh version 2 creation and the drift error naming the upgrade path when initialization is disabled

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Custom Oracle store implementation | Yes (extension point) | No sample third-party store test | Contract | Low |
| SQL Server storage adapter | Partial | EF Core adapter works with the SQL Server provider; a dedicated adapter is not shipped | N/A | N/A |
| Multi-worker in-memory production simulation | No (by design) | In-memory is single-process | N/A | N/A |

### Out-of-Scope Use Cases

- SQL Server and SQLite production stores
- Automatic deep storage payload dedup refactor
- Cross-process in-memory durability
