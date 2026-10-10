# Outbox EF Core Adapter

- **ID**: `storage.outbox.adapter.efcore`
- **Name**: Outbox EF Core adapter
- **Maturity**: GA
- **Summary**: Entity Framework Core outbox store with factory-created operation contexts and transactional enqueue through a typed writer, existing context, or SaveChanges interceptor.

## What It Does

`EfCoreOutboxStore` implements all outbox roles on `DbSet<OutboxMessageEntity>`. Registration:

```csharp
outbox.UseEntityFrameworkCoreStorage(o => o.UseDbContext<AppDbContext>());
```

Default auto-commit operations create and dispose a context through `IDbContextFactory<TContext>`. Caller-owned transactional patterns are documented in [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md) and `storage.transactional.writes`.

Supports PostgreSQL, SQL Server, MySQL, SQLite, and in-memory EF providers for leasing. MySQL uses a chronological index with skip-locked `READ COMMITTED` transactions. SQLite uses UTC-tick timestamp columns and serializable file transactions.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `EfCoreOutboxStore` | All outbox store roles on `DbSet<OutboxMessageEntity>` |
| `ITransactionalOutbox<TContext>` | Typed transactional enqueue |
| `LiteBusOutboxSaveChangesInterceptor` | Stage enqueues until `SaveChanges` |
| `OutboxMessageEntity` | EF persistence entity |
| `IOutboxDbContext` | DbContext marker interface |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IOutboxStore.EnqueueAsync` | Auto-commit operation context (calls `SaveChanges`) |
| `ITransactionalOutbox<TContext>.EnqueueAsync` | Command handlers in EF transaction |
| `EfCoreOutboxStore.UseExistingDbContext` | Manual staging on caller context |
| `IOutboxLeaseStore.LeasePendingAsync` | Outbox processor |

### Registration

```csharp
outbox.UseEntityFrameworkCoreStorage(o =>
{
    o.UseDbContext<AppDbContext>();
    o.EnableSaveChangesInterceptor();
});
```

- Interceptor must be registered on both LiteBus module and `DbContextOptions`.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `EntityFrameworkCoreOutboxStoreOptions` | Schema, table, lease duration |
| `UseDbContext<TContext>()` | Selects the application-registered `IDbContextFactory<TContext>` |
| `EnableSaveChangesInterceptor()` / `EnforceTransactionalSetup()` | Transactional enqueue |
| `GetModelBuilderConfiguration()` | Application-owned EF model |

### Extension Points

- Supported relational providers via `EfCoreStorageProvider`.
- `UseExistingDbContext` for read/write split or manual unit-of-work patterns.

## Packages

| Package | Dependency role |
| --- | --- |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | Feature bridge |
| `LiteBus.Storage.EntityFrameworkCore` | Technology adapter (transitive) |

## Requires

- `IOutboxDbContext` on application `DbContext`
- `AddDbContextFactory<TContext>(...)` in application composition
- Application-owned EF migrations
- Outbox dispatch before processor

## Invariants

- Schema and table names in options must match `OnModelCreating` configuration.
- Interceptor pattern requires registration on both LiteBus module and `DbContext` options.

## Non-Goals

- LiteBus-driven EF migrations
- Broker publish

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.outbox.status` | EF diagnostics role |
| `LiteBus.Outbox` | `litebus.outbox.processor.state` | Observable gauge | Processor loop | State enum | Processor on EF store |
| `LiteBus.Outbox` | `litebus.outbox.processor.leases_acquired` | Counter | Processor pass |: | EF lease SQL |
| `LiteBus.Outbox` | `litebus.outbox.processor.dispatch_duration` | Histogram | After publication |: | Dispatch duration |
| `LiteBus.Outbox` | `litebus.outbox.processor.lease_lost` | Counter | Lease renewal failure |: | Long publication |
| `LiteBus.Outbox` | `litebus.outbox.processor.persist_skipped` | Counter | Skipped persist |: | Lost lease |
| `LiteBus.Outbox` | `litebus.outbox.processor.persist_rejected` | Counter | Rejected persist |: | Competing workers |
| `LiteBus.Outbox` | `litebus.outbox.cleanup.errors` | Counter | Retention failure |: | EF retention store |

Register through `AddLiteBusOutboxMetrics()`. Constants on `LiteBusOutboxTelemetry`.

## Deep Docs

- [Outbox Entity Framework Core storage](../../integrations/outbox-ef-core-storage.md)
- [Domain events and unit of work](../../concepts/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles`

- **Use case**: Register single outbox store instance for all roles
- **Test kind**: Unit
- **Description**: Build EF outbox storage module
- **Behavior**: Resolve all outbox store interfaces
- **Expected outcome**: Same instance for every role
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_WithEnforceTransactionalSetupWithoutInterceptor_ShouldThrowOnBuild`

- **Use case**: Throw when transactional setup enforced without interceptor
- **Test kind**: Unit
- **Description**: Enforce without interceptor
- **Behavior**: Module build
- **Expected outcome**: Configuration exception
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxStoreContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: In-memory EF outbox contract suite
- **Test kind**: Contract (unit)
- **Description**: Full contract against EF in-memory provider
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxStorePostgreSqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: PostgreSQL EF outbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Migrated PostgreSQL EF store contract tests
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreOutboxStoreSqlServerContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: SQL Server EF outbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Migrated SQL Server EF store contract tests
- **Behavior**: Inherited contract tests
- **Expected outcome**: Parity with shared contract suite
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreOutboxProcessorEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore`

- **Use case**: Processor E2E publishes through EF outbox store
- **Test kind**: Integration
- **Description**: Enqueue and process via EF store
- **Behavior**: Processor with in-process dispatcher
- **Expected outcome**: Event published; row terminal
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreOutboxFindExistingTests.EnqueueAsync_when_id_and_idempotency_key_match_different_rows_should_prefer_message_id_row`

- **Use case**: Prefer message id row when id and idempotency match different rows
- **Test kind**: Unit
- **Description**: Two rows with overlapping identifiers
- **Behavior**: Enqueue resolving existing row
- **Expected outcome**: Message id match wins over idempotency key
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxStoreConcurrencyTests.PersistAsync_WhenTwoWorkersPersistSameEnvelopeConcurrently_ShouldApplyExactlyOnce`

- **Use case**: Concurrent persist applies exactly once
- **Test kind**: Integration
- **Description**: Two workers persist same leased envelope
- **Behavior**: Concurrent `PersistAsync`
- **Expected outcome**: Single terminal update
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: Map version 1 outbox columns in model
- **Test kind**: Unit
- **Description**: Model extension configuration
- **Behavior**: Inspect EF model
- **Expected outcome**: v1 column layout mapped
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapEntityColumns`

- **Use case**: Map outbox entity columns in model
- **Test kind**: Unit
- **Description**: Property and column mappings
- **Behavior**: Model builder extension
- **Expected outcome**: Topic, payload, scheduling columns mapped
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxSaveChangesInterceptorIntegrationTests.SaveChangesAsync_ShouldPersistOutboxMessage_WhenTransactionCommits`

- **Use case**: Interceptor persists outbox on commit
- **Test kind**: Integration
- **Description**: Enqueue via interceptor and commit
- **Behavior**: Successful `SaveChanges` and commit
- **Expected outcome**: Outbox row in database
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `TransactionalOutboxEnqueueTests.EnqueueAsync_should_stage_envelope_with_contract_and_metadata`

- **Use case**: Transactional enqueue stages envelope with metadata
- **Test kind**: Unit
- **Description**: `ITransactionalOutbox` before save
- **Behavior**: Enqueue then inspect staged state
- **Expected outcome**: Contract and metadata staged
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests`

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldCommitDomainAndOutboxTogether`

- **Use case**: EF use-existing DbContext commits outbox with domain
- **Test kind**: Integration
- **Description**: Shared DbContext transaction
- **Behavior**: SaveChanges and commit
- **Expected outcome**: Domain and outbox rows committed
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreOutboxStoreMySqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: MySQL EF outbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Full outbox contract suite against MySQL 8.4 through Pomelo
- **Behavior**: Ordered concurrent leasing, append outcomes, fencing, queries, and purge
- **Expected outcome**: Same outcomes as the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStoreSqliteContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: File-backed SQLite outbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Full outbox contract suite against independent contexts sharing one database file
- **Behavior**: Serializable claims, UTC-tick timestamp queries, append outcomes, and fencing
- **Expected outcome**: Same outcomes as the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

### Untested

- Read/write split DbContext with explicit `UseExistingDbContext` integration test.

### Out-of-Scope

- LiteBus-driven EF migrations.
- Broker publish (dispatch axis).
