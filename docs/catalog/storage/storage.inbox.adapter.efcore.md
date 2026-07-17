# Inbox EF Core Adapter

- **ID**: `storage.inbox.adapter.efcore`
- **Name**: Inbox EF Core adapter
- **Maturity**: GA
- **Summary**: Entity Framework Core inbox store on a shared `DbContext`, with provider-specific leasing for PostgreSQL, SQL Server, and MySQL.

## What It Does

`EfCoreInboxStore` implements all inbox store roles against `DbSet<InboxMessageEntity>`. Registration:

```csharp
inbox.UseEntityFrameworkCoreStorage(o => o.UseDbContext<AppDbContext>());
```

`AppDbContext` implements `IInboxDbContext`. Default table: `public.litebus_inbox_messages`.

Transactional patterns:

- `ITransactionalInbox<TContext>` for typed writer (`storage.transactional.writes`)
- `UseExistingDbContext` for manual staging
- `EnableSaveChangesInterceptor()` for interceptor-based accept before `SaveChanges`

LiteBus does not create or migrate EF schema (see `storage.efcore.schema-ownership`).

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `EfCoreInboxStore` | All inbox store roles on `DbSet<InboxMessageEntity>` |
| `ITransactionalInbox<TContext>` | Typed transactional accept |
| `LiteBusInboxSaveChangesInterceptor` | Stage accepts until `SaveChanges` |
| `InboxMessageEntity` | EF persistence entity |
| `IInboxDbContext` | DbContext marker interface |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IInboxStore.AddAsync` | Auto-commit singleton store (calls `SaveChanges`) |
| `ITransactionalInbox<TContext>.AcceptAsync` | Command handlers in EF transaction |
| `IInboxLeaseStore.LeasePendingAsync` | Inbox processor |
| Interceptor staging | `SaveChanges` on enrolled `DbContext` |

### Registration

```csharp
inbox.UseEntityFrameworkCoreStorage(o =>
{
    o.UseDbContext<AppDbContext>();
    o.EnableSaveChangesInterceptor();
});
```

- Single scoped/singleton store instance registered for all inbox store roles.
- Interceptor must also be added to `DbContextOptions` in application startup.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `EntityFrameworkCoreInboxStoreOptions` | Schema, table, lease duration, conflict mode |
| `UseDbContext<TContext>()` | Factory for store `DbContext` |
| `EnableSaveChangesInterceptor()` / `EnforceTransactionalSetup()` | Transactional accept path |
| `GetModelBuilderConfiguration()` | Application-owned EF model (see schema ownership) |

### Extension Points

- Any EF relational provider supported by `EfCoreStorageProvider` (PostgreSQL, SQL Server, MySQL; SQLite for tests).
- `UseExistingDbContext` for caller-owned context in handlers.

## Packages

| Package | Layer |
| --- | --- |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | 4 |
| `LiteBus.Storage.EntityFrameworkCore` | 3 (transitive) |

## Requires

- EF Core relational provider
- Application migrations including inbox entity configuration
- Dispatch registered before processor enablement

## Invariants

- Model configuration must match store expectations (column names, indexes, filtered idempotency index).
- Singleton auto-commit store uses scoped factory; transactional paths use caller context.

## Non-Goals

- Automatic EF migrations from LiteBus
- PostgreSQL NOTIFY (use PostgreSQL adapter for NOTIFY)
- SQLite production guidance (supported for tests via provider resolver)

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.inbox.status` | EF store diagnostics role |
| `LiteBus.Inbox` | `litebus.inbox.processor.state` | Observable gauge | Processor loop | State enum | Processor on EF store |
| `LiteBus.Inbox` | `litebus.inbox.processor.leases_acquired` | Counter | Processor pass |: | EF lease SQL |
| `LiteBus.Inbox` | `litebus.inbox.processor.dispatch_duration` | Histogram | After dispatch |: | Handler execution |
| `LiteBus.Inbox` | `litebus.inbox.processor.lease_lost` | Counter | Lease renewal failure |: | Long dispatch |
| `LiteBus.Inbox` | `litebus.inbox.processor.persist_skipped` | Counter | Skipped terminal persist |: | Lost lease |
| `LiteBus.Inbox` | `litebus.inbox.processor.persist_rejected` | Counter | Rejected persist |: | Competing workers |
| `LiteBus.Inbox` | `litebus.inbox.cleanup.errors` | Counter | Retention failure |: | EF retention store |

Register through `AddLiteBusInboxMetrics()`. Constants on `LiteBusInboxTelemetry`.

## Deep Docs

- [Inbox Entity Framework Core storage](../../integrations/inbox-ef-core-storage.md)
- [Transactional messaging writes](../../reliable-messaging/transactional-writes.md)

## Test Coverage

### Covered

#### `EfCoreInboxStorageModuleTests.AddEfCoreInboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles`

- **Use case**: Register single inbox store instance for all roles
- **Test kind**: Unit
- **Description**: Build EF inbox storage module
- **Behavior**: Resolve all inbox store interfaces
- **Expected outcome**: Same instance for every role
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxStorageModuleTests.AddEfCoreInboxStorage_WithSaveChangesInterceptor_ShouldRegisterTransactionalInbox`

- **Use case**: Register transactional inbox with SaveChanges interceptor
- **Test kind**: Unit
- **Description**: Module with interceptor enabled
- **Behavior**: Resolve `ITransactionalInbox<TContext>`
- **Expected outcome**: Transactional inbox registered
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxStorageModuleTests.AddEfCoreInboxStorage_WithEnforceTransactionalSetupWithoutInterceptor_ShouldThrowOnBuild`

- **Use case**: Throw when transactional setup enforced without interceptor
- **Test kind**: Unit
- **Description**: Enforce without interceptor
- **Behavior**: Module build
- **Expected outcome**: Configuration exception
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxStoreContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: In-memory EF inbox contract suite
- **Test kind**: Contract (unit)
- **Description**: Full contract against EF in-memory provider
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxStorePostgreSqlContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: PostgreSQL EF inbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Migrated PostgreSQL EF store contract tests
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreInboxStoreSqlServerContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: SQL Server EF inbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Migrated SQL Server EF store contract tests
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreInboxProcessorEndToEndTests.ProcessPendingAsync_ShouldExecuteScheduledCommandThroughEfCoreStore`

- **Use case**: Processor E2E through EF inbox store
- **Test kind**: Integration
- **Description**: Accept and process via EF store
- **Behavior**: Processor pass with handler
- **Expected outcome**: Command completed in database
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreInboxStoreParityTests.LeasePendingAsync_FiltersByTenantId`

- **Use case**: Lease filters by tenant on EF store
- **Test kind**: Unit
- **Description**: Multi-tenant pending rows
- **Behavior**: Lease with tenant id
- **Expected outcome**: Only matching tenant leased
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: Map version 1 inbox columns in model
- **Test kind**: Unit
- **Description**: Model extension configuration
- **Behavior**: Inspect EF model
- **Expected outcome**: v1 column layout mapped
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapTenantScopedIdempotencyIndex`

- **Use case**: Map tenant-scoped idempotency index
- **Test kind**: Unit
- **Description**: Index configuration from model extension
- **Behavior**: Inspect indexes
- **Expected outcome**: Filtered unique idempotency index present
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `InboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapCustomSchemaAndTable`

- **Use case**: Map custom schema and table in model
- **Test kind**: Unit
- **Description**: Custom names in model extension
- **Behavior**: Inspect table mapping
- **Expected outcome**: Custom qualified table name
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreInboxSaveChangesInterceptorRollbackTests.SaveChangesAsync_ShouldNotPersistInboxMessage_WhenTransactionRollsBack`

- **Use case**: Interceptor rollback does not persist inbox row
- **Test kind**: Integration
- **Description**: Accept via interceptor then rollback
- **Behavior**: Rolled-back EF transaction
- **Expected outcome**: No inbox row in database
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `TransactionalInboxAcceptTests.AcceptAsync_should_stage_envelope_with_contract_and_metadata`

- **Use case**: Transactional accept stages envelope with metadata
- **Test kind**: Unit
- **Description**: `ITransactionalInbox` before save
- **Behavior**: Accept then inspect staged state
- **Expected outcome**: Contract and metadata staged
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests`

### Untested

- MySQL/Pomelo provider inbox contract suite (lease SQL exists; no dedicated contract test project entry).

### Out-of-Scope

- LiteBus-driven EF migrations or `EnsureCreated`.
- PostgreSQL NOTIFY (use PostgreSQL Npgsql adapter).
- SQLite as production guidance (test-oriented only).
