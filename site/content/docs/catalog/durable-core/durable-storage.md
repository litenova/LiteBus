# Durable Storage

**ID:** `durable-core.durable-storage`
**Maturity:** GA

## Summary

Durable storage persists inbox and outbox envelopes through store interfaces split by role, with PostgreSQL, EF Core, and in-memory implementations.

## What It Does

Storage adapters implement narrow roles: append (writer), lease (processor), state persist (processor), dead letter, retention, diagnostics, query, and purge. A single class typically implements all roles on one table set. PostgreSQL inbox and saga components use schema version 1. The PostgreSQL outbox component uses schema version 2, which adds the nullable `handler` column for per-handler delivery. `EnsureAsync` upgrades a version 1 table automatically. EF Core schemas are owned by the application. The shared PostgreSQL primitives are in `LiteBus.Storage.PostgreSql`.

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
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory`, `LiteBus.Outbox.Storage.InMemory` | Process-local and single-worker |
| `UsePostgreSqlStorage(configure)` | `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.PostgreSql` | PostgreSQL schema v1 (inbox), v2 (outbox) |
| `UseEntityFrameworkCoreStorage(configure)` | `LiteBus.Inbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.EntityFrameworkCore` | Application-owned EF migrations |
| `UseDataSource(NpgsqlDataSource)` | `LiteBus.Storage.PostgreSql` | Shared connection pool across axes |
| `EnsureSchemaCreationOnStartup()` | PostgreSQL storage modules | Dev-only schema bootstrap through a startup task |

Register storage only inside the **`AddInbox(...)`** and **`AddOutbox(...)`** builders. Calling the same storage extension twice on one builder throws **`DurableStorageConfigurationException`**.

### Configuration

- For PostgreSQL, set the connection string or a shared **`NpgsqlDataSource`**, the schema name, and the table prefix options on the module builders
- For EF Core, set the **`DbContext`** type, schema ownership, and transactional interceptor pairing (see the transactional-writes capability)
- In-memory storage needs no external configuration. It uses a process-wide lock and does not support multiple workers

### Runtime Dependencies

- **`IMessageContractRegistry`**: Provides the contract name and version before appending
- **`IInboxEnvelopeFactory`** and **`IOutboxEnvelopeFactory`**: Serialize envelopes at the writer boundary
- Use one data source per database when sharing PostgreSQL between the inbox and outbox modules

### Extension Points

- Custom stores implement the role interfaces and register through the storage module pattern (see [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md))
- The shared PostgreSQL infrastructure in **`LiteBus.Storage.PostgreSql`** provides ambient transactions and NOTIFY work signals

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
| `LiteBus.Storage.PostgreSql` | Shared Npgsql helpers and ambient transaction provider |
| `LiteBus.Outbox.Storage.Marten` | Queued transactional outbox writer on the current Marten session |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Axis-specific stores |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Store interface definitions |

## Requires

- Storage is registered inside the `AddInbox` or `AddOutbox` builder
- Contracts are registered before writes
- One data source per database when sharing PostgreSQL between axes

## Invariants

- The default table is `litebus_inbox_messages` (or the outbox equivalent), with `message_id` as the primary key
- The in-memory store uses a process-wide lock. It does not support multi-worker production simulation
- Custom stores implement the roles and use envelope factories for serialization
- PostgreSQL inbox and saga components use schema version 1. The outbox component uses schema version 2, with automatic upgrade from version 1 under `EnsureAsync`

## Non-Goals

- A dedicated SQL Server store. The EF Core adapter supports the SQL Server provider, and a dedicated adapter is planned (see [Roadmap](../../roadmap/README.md))
- A SQLite production store
- A shared storage implementation across adapters. Each adapter owns its provider-specific behavior

## Observability

### Schema Diagnostic Probes

- **Contract**: **`IDiagnosticCheck`**, registered through storage modules
- **Kind**: Framework-neutral diagnostic probe (platform contract)
- **When run**: Host startup and manual diagnostic runner invocations run it
- **Signal**: Healthy when the required tables and columns exist, degraded or unhealthy on drift
- **Registration**: `configuration.RegisterDiagnosticCheck(typeof(...), name)` inside storage module **`Build()`**
- **Operational note**: Wire the probes to ASP.NET health checks through the hosting axis. LiteBus ships probes, not HTTP endpoints

### Schema Info API

- **Member**: **`GetSchemaInfoAsync`** on **`IInboxDiagnosticsStore`** and **`IOutboxDiagnosticsStore`**
- **Kind**: Management query (no dedicated meter)
- **When called**: Operator tooling and management endpoints call it
- **Returns**: **`StoreSchemaInfo`** with version and table metadata
- **Operational note**: Use it before a deploy to confirm the current schema version, column types, and indexes

### Queue Depth Gauges

- **Instrument**: `litebus.inbox.queue.depth` and `litebus.outbox.queue.depth`
- **Constants**: `LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`
- **Kind**: Observable gauge
- **Tags**: `litebus.inbox.status` and `litebus.outbox.status`
- **Source**: The diagnostics store role reads status counts during the metric scrape
- **Registration**: `AddLiteBusInboxMetrics()` or `AddLiteBusOutboxMetrics()`

### PostgreSQL NOTIFY Work Signal

- **Kind**: Internal wake mechanism (not a public meter)
- **When emitted**: A row insert triggers NOTIFY, and the processor may wake before the poll timeout
- **Operational note**: This reduces latency after an accept. The poll interval still applies as a fallback

### Retention Cleanup Errors

- **Instrument**: `litebus.inbox.cleanup.errors` and `litebus.outbox.cleanup.errors`
- **Constants**: `LiteBusInboxTelemetry.CleanupErrorInstrumentName`, `LiteBusOutboxTelemetry.CleanupErrorInstrumentName`
- **Kind**: Counter
- **When incremented**: A retention background service purge fails

## References

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
- **Description**: The full contract suite, run per in-memory and adapter implementation
- **Behavior**: All `[Fact]` methods in **`InboxStoreContractTests`**
- **Expected outcome**: Parity across in-memory implementations and production adapters
- **Remarks**: Runs per adapter project

#### `OutboxStoreContractTests` (All `[Fact]` Methods)

- **Use case**: When outbox store roles are exercised, behavior matches across backends
- **Test kind**: Contract
- **Description**: The full contract suite for outbox roles
- **Behavior**: All `[Fact]` methods in **`OutboxStoreContractTests`**
- **Expected outcome**: Parity across backends
- **Remarks**: Runs per adapter project

#### `InboxRetentionStoreContractTests` (All Methods)

- **Use case**: When retention runs on completed inbox rows, eligible rows are deleted per cutoff semantics
- **Test kind**: Contract
- **Description**: The retention role contract suite
- **Behavior**: **`DeleteCompletedOlderThanAsync`** and related retention methods run
- **Expected outcome**: Eligible rows are deleted, and recent rows are retained
- **Remarks**: Store contract suite

#### `OutboxRetentionStoreContractTests` (All Methods)

- **Use case**: When retention runs on published outbox rows, eligible rows are purged
- **Test kind**: Contract
- **Description**: The outbox retention role contract suite
- **Behavior**: **`DeletePublishedOlderThanAsync`** and related methods run
- **Expected outcome**: Old published rows are removed
- **Remarks**: Store contract suite

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: When in-memory inbox storage is registered, all store role interfaces resolve from DI
- **Test kind**: Unit
- **Description**: Module registration through the inbox builder
- **Behavior**: **`UseInMemoryStorage()`** runs on the inbox module
- **Expected outcome**: All store interfaces are registered
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Inbox/`)

#### `InMemoryOutboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: When in-memory outbox storage is registered, all store role interfaces resolve from DI
- **Test kind**: Unit
- **Description**: Module registration through the outbox builder
- **Behavior**: **`UseInMemoryStorage()`** runs on the outbox module
- **Expected outcome**: All store interfaces are registered
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Outbox/`)

#### `PostgreSqlInboxSchemaTests.EnsureAsync_ShouldCreateSchemaAndBeIdempotent`

- **Use case**: When the PostgreSQL inbox schema ensure runs, tables are created and repeat calls are idempotent
- **Test kind**: Integration
- **Description**: Schema bootstrap for inbox tables
- **Behavior**: **`EnsureAsync`** runs twice
- **Expected outcome**: Tables are created once, and the second call succeeds without error
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlOutboxSchemaTests.EnsureAsync_ShouldCreateSchemaAndBeIdempotent`

- **Use case**: When the PostgreSQL outbox schema ensure runs, DDL is idempotent
- **Test kind**: Integration
- **Description**: Schema bootstrap for outbox tables
- **Behavior**: **`EnsureAsync`** runs twice
- **Expected outcome**: The DDL is idempotent
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaDriftTests.InboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns`

- **Use case**: When inbox schema validation detects missing columns, the error lists the required columns
- **Test kind**: Integration
- **Description**: Schema drift detection for the inbox
- **Behavior**: **`ValidateAsync`** runs against an incomplete schema
- **Expected outcome**: An actionable drift error with the missing column names
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaDriftTests.OutboxValidateAsync_WhenRequiredColumnMissing_ShouldThrowWithMissingColumns`

- **Use case**: When outbox schema validation detects missing columns, the error lists the required columns
- **Test kind**: Integration
- **Description**: Schema drift detection for the outbox
- **Behavior**: **`ValidateAsync`** runs against an incomplete schema
- **Expected outcome**: The missing columns are reported
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlSchemaHostingTests.InboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup`

- **Use case**: When schema creation on startup is enabled, the hosted startup task creates the inbox schema
- **Test kind**: Integration
- **Description**: The **`EnsureSchemaCreationOnStartup()`** startup task
- **Behavior**: A host run with the schema initializer enabled
- **Expected outcome**: The schema is present after startup
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: When the EF Core inbox model is configured, the v1 column mapping matches the expected schema
- **Test kind**: Unit
- **Description**: The EF model builder configuration
- **Behavior**: **`GetModelBuilderConfiguration`** runs
- **Expected outcome**: The expected v1 columns are mapped
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Inbox/`)

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: When the EF Core outbox model is configured, the v1 column mapping matches the expected schema
- **Test kind**: Unit
- **Description**: The EF model builder configuration
- **Behavior**: **`GetModelBuilderConfiguration`** runs
- **Expected outcome**: The expected v1 columns are mapped
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles`

- **Use case**: When EF outbox storage registers, one store instance satisfies all role interfaces
- **Test kind**: Unit
- **Description**: DI registration for the EF outbox store
- **Behavior**: The module builds and services resolve
- **Expected outcome**: A single instance is registered for the writer, lease, and state roles
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `PostgreSqlModuleRegistrationTests.AddPostgreSqlInboxStorage_ShouldRegisterWriterLeaseAndStateRoles`

- **Use case**: When the PostgreSQL inbox storage module builds, the writer, lease, and state roles register
- **Test kind**: Integration
- **Description**: PostgreSQL module registration
- **Behavior**: **`UsePostgreSqlStorage`** runs on the inbox builder
- **Expected outcome**: The writer, lease, and state interfaces are available
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlModuleRegistrationTests.SharedDataSource_inbox_and_outbox_modules_should_resolve_same_instance`

- **Use case**: When inbox and outbox modules share a data source, they resolve the same pool instance
- **Test kind**: Integration
- **Description**: The **`UseDataSource(NpgsqlDataSource)`** shared configuration
- **Behavior**: Both modules register with the same data source
- **Expected outcome**: The same singleton pool instance
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `PostgreSqlInboxWorkSignalNotifyTests.WaitForWorkOrDelayAsync_when_row_inserted_should_wake_before_poll_timeout`

- **Use case**: When a row is inserted, PostgreSQL NOTIFY wakes the processor before the poll timeout elapses
- **Test kind**: Integration
- **Description**: The work signal NOTIFY path
- **Behavior**: Insert a row while the processor waits
- **Expected outcome**: An early wake before the poll timeout
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxStoreParityTests.LeasePendingAsync_FiltersByTenantId`

- **Use case**: When the EF outbox lease runs with a tenant filter, only matching tenant rows are leased
- **Test kind**: Unit
- **Description**: The tenant filter in the EF lease query
- **Behavior**: **`LeasePendingAsync`** runs with tenant-scoped processor options
- **Expected outcome**: Only scoped rows appear in the lease batch
- **Remarks**: `LiteBus.Storage.UnitTests` (`EntityFrameworkCore/Outbox/`)

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_WhenCalledTwiceOnBuilder_ShouldThrow`

- **Use case**: When in-memory storage is registered twice on the same builder, configuration fails at compose time
- **Test kind**: Unit
- **Description**: The builder guard against duplicate storage registration
- **Behavior**: Two **`UseInMemoryStorage()`** calls on one inbox builder
- **Expected outcome**: **`LiteBusConfigurationException`** reporting that inbox storage is already configured
- **Remarks**: `LiteBus.Storage.UnitTests` (`InMemory/Inbox/`). The builder throws the derived `DurableStorageConfigurationException`

#### `PostgreSqlOutboxSchemaUpgradeTests.EnsureAsync_WhenVersion1TableExists_ShouldUpgradeToVersion2AndPreserveRows`

- **Use case**: When a version 1 outbox table exists, schema ensure upgrades it to version 2 without losing rows
- **Test kind**: Integration
- **Description**: An existing version 1 table with stored envelopes
- **Behavior**: **`EnsureAsync`** runs against the old table shape
- **Expected outcome**: The nullable `handler` column is added, and existing rows are preserved
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`). Companion tests cover fresh version 2 creation and the drift error naming the upgrade path when initialization is disabled

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Custom Oracle store implementation | Yes (extension point) | No sample third-party store test | Contract | Low |
| SQL Server storage adapter | Partial | The EF Core adapter works with the SQL Server provider, but a dedicated adapter is not shipped | N/A | N/A |
| Multi-worker in-memory production simulation | No (by design) | In-memory storage is single-process | N/A | N/A |

### Out-of-Scope Use Cases

- SQL Server and SQLite production stores
- Automatic deep storage payload dedup refactor
- Cross-process in-memory durability
