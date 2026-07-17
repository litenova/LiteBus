# EF Core Schema Ownership

- **ID**: `storage.efcore.schema-ownership`
- **Name**: EF Core schema ownership
- **Maturity**: GA
- **Summary**: Documents the EF Core storage contract where applications own all DDL through EF migrations. LiteBus supplies model configuration helpers but never runs migrations or startup schema ensure.

## What It Does

For inbox and outbox EF adapters:

1. Call `GetModelBuilderConfiguration()` from `InboxEntityFrameworkCoreModelExtensions` or `OutboxEntityFrameworkCoreModelExtensions` in `OnModelCreating`.
2. Align `EntityFrameworkCoreInboxStoreOptions` and `EntityFrameworkCoreOutboxStoreOptions` schema and table names with fluent configuration.
3. Generate and apply EF migrations as part of normal application deployment.

PostgreSQL schema initializers (`PostgreSql*SchemaInitializer`) are not registered for EF storage. Diagnostics `GetSchemaInfoAsync` reports expected LiteBus schema version; recorded version reflects application migration state.

Column layouts mirror PostgreSQL v1 scripts (snake_case columns, filtered idempotency index, lease index, outbox topic index).

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration` | Inbox entity, indexes, column types |
| `OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration` | Outbox entity, indexes, column types |
| `InboxMessageEntity` / `OutboxMessageEntity` | Mapped persistence entities |
| `EfCoreRelationalModelColumnTypes` | Provider-appropriate JSON column types |
| `EntityFrameworkCoreInboxStoreOptions` / `EntityFrameworkCoreOutboxStoreOptions` | Schema and table naming |
| `IInboxDbContext` / `IOutboxDbContext` | DbContext marker interfaces |

### Invocation

| Step | Owner |
| --- | --- |
| `OnModelCreating` + model extensions | Application `DbContext` |
| `dotnet ef migrations add` / deploy | Application CI/CD |
| `GetSchemaInfoAsync` | LiteBus diagnostics store at runtime |

### Registration

- EF storage modules do not register schema startup tasks or `EnsureCreated`.
- Model extensions are called once during application model building, not at LiteBus module build.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `EntityFrameworkCoreInboxStoreOptions.SchemaName` / `TableName` | Must match fluent configuration |
| `EntityFrameworkCoreOutboxStoreOptions.SchemaName` / `TableName` | Must match fluent configuration |
| Provider hint on `GetModelBuilderConfiguration` | PostgreSQL `TEXT` for payload and nullable `jsonb` for trace context |

### Extension Points

- Applications own all migration files and deployment order.
- Custom column names require aligned options and fluent overrides (not recommended; breaks contract tests).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | Inbox model extensions |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | Outbox model extensions |
| `LiteBus.Storage.EntityFrameworkCore` | Shared column type helpers |

## Requires

- EF Core migrations pipeline in the application
- Matching options between LiteBus registration and `OnModelCreating`

## Invariants

- Mismatch between options and fluent config causes runtime query failures or drift exceptions on diagnostics.
- LiteBus never calls `Database.Migrate()` or `EnsureCreated()`.

## Non-Goals

- Rendering EF migrations from LiteBus CLI
- Automatic sync from PostgreSQL embedded SQL to EF model
- Shared metadata table `litebus_schema_versions` on EF (PostgreSQL Npgsql path only)

## Observability

| Signal | Source | Notes |
| --- | --- | --- |
| `GetSchemaInfoAsync` | `IInboxDiagnosticsStore` / `IOutboxDiagnosticsStore` | Reports expected LiteBus v1 layout and configured object names |
| `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth` | `LiteBusInboxTelemetry` / `LiteBusOutboxTelemetry` | Requires matching schema; query failures prevent meaningful counts |
| Application migration logs | Host deployment | Actual DDL state; LiteBus does not emit migration events |

No schema-specific OpenTelemetry instruments ship for EF ownership.

## Deep Docs

- [Inbox Entity Framework Core storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox Entity Framework Core storage](../../integrations/outbox-ef-core-storage.md)
- [PostgreSQL schema management: EF section](../../integrations/postgresql-schema-management.md)

## Test Coverage

### Covered

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: Map inbox version 1 columns via model extension
- **Test kind**: Unit
- **Description**: Apply inbox model configuration to in-memory model
- **Behavior**: `GetModelBuilderConfiguration`
- **Expected outcome**: All v1 columns mapped with expected names and types
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapTenantScopedIdempotencyIndex`

- **Use case**: Map inbox tenant-scoped idempotency index
- **Test kind**: Unit
- **Description**: Model builder with default inbox extension
- **Behavior**: Inspect index configuration
- **Expected outcome**: Filtered unique index on tenant plus idempotency key
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldUsePostgreSqlCanonicalDefaults`

- **Use case**: Map inbox PostgreSQL canonical defaults
- **Test kind**: Unit
- **Description**: PostgreSQL provider hint on model extension
- **Behavior**: Configure with PostgreSQL defaults
- **Expected outcome**: Schema and table defaults match PostgreSQL v1 scripts
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapCustomSchemaAndTable`

- **Use case**: Map inbox custom schema and table names
- **Test kind**: Unit
- **Description**: Non-default schema and table in extension call
- **Behavior**: Custom names passed to configuration delegate
- **Expected outcome**: Entity mapped to custom qualified table
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapTraceContextAsJsonbWhenProviderSpecified`

- **Use case**: Map inbox trace_context as jsonb when provider specified
- **Test kind**: Unit
- **Description**: PostgreSQL provider on model extension
- **Behavior**: Inspect trace_context column type
- **Expected outcome**: `jsonb` column type configured
- **Remarks**: Uses `EfCoreRelationalModelColumnTypes`; `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldLeaveTraceContextProviderNeutralByDefault`

- **Use case**: Leave inbox trace_context provider-neutral by default
- **Test kind**: Unit
- **Description**: Model extension without provider hint
- **Behavior**: Default trace_context mapping
- **Expected outcome**: No provider-specific column type forced
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: Map outbox version 1 columns via model extension
- **Test kind**: Unit
- **Description**: Apply outbox model configuration
- **Behavior**: `GetModelBuilderConfiguration`
- **Expected outcome**: All v1 outbox columns mapped
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapEntityColumns`

- **Use case**: Map outbox entity columns
- **Test kind**: Unit
- **Description**: Inspect outbox entity property mappings
- **Behavior**: Model builder configuration
- **Expected outcome**: Topic, payload, scheduling columns mapped
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapTraceContextAsJsonbWhenProviderSpecified`

- **Use case**: Map outbox trace_context as jsonb when provider specified
- **Test kind**: Unit
- **Description**: PostgreSQL provider on outbox model extension
- **Behavior**: Inspect trace_context column
- **Expected outcome**: `jsonb` type configured
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxStorePostgreSqlContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: EF integration applies migrations before inbox contract tests
- **Test kind**: Contract (integration)
- **Description**: Fixture applies EF migrations then runs full inbox contract suite
- **Behavior**: All inherited contract tests on migrated PostgreSQL EF store
- **Expected outcome**: Contract parity proves schema matches v1 expectations
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreOutboxStorePostgreSqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: EF integration applies migrations before outbox contract tests
- **Test kind**: Contract (integration)
- **Description**: Fixture applies EF migrations then runs full outbox contract suite
- **Behavior**: Inherited contract tests on migrated PostgreSQL EF store
- **Expected outcome**: Contract parity proves schema matches v1 expectations
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

### Untested

- LiteBus CLI to generate EF migrations from embedded PostgreSQL SQL.
- Automatic sync from PostgreSQL v1 scripts to EF model when scripts change.

### Out-of-Scope

- LiteBus calling `Database.Migrate()` or `EnsureCreated()`.
- Shared `litebus_schema_versions` metadata table on EF path (PostgreSQL Npgsql path only).
- Rendering EF migrations inside NuGet packages.
