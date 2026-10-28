# Outbox EF Core Adapter

- **ID**: `storage.outbox.adapter.efcore`
- **Name**: Outbox EF Core adapter
- **Maturity**: GA
- **Summary**: Outbox store built on Entity Framework Core. It creates operation contexts through a factory and enqueues transactionally through a typed writer, an existing context, or a SaveChanges interceptor.

## What It Does

`EfCoreOutboxStore` implements all outbox roles on `DbSet<OutboxMessageEntity>`. Register it with:

```csharp
outbox.UseEntityFrameworkCoreStorage(o => o.UseDbContext<AppDbContext>());
```

Default auto-commit operations create and dispose a context through `IDbContextFactory<TContext>`. For caller-owned transactional patterns, see [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md) and `storage.transactional.writes`.

The store supports PostgreSQL, SQL Server, MySQL, SQLite, and in-memory EF providers for leasing. MySQL uses a chronological index with skip-locked `READ COMMITTED` transactions. SQLite uses UTC-tick timestamp columns and serializable file transactions.

`OutboxMessageEntity.Handler` maps to the nullable `handler` column, so the EF model carries the per-handler delivery target the same way the PostgreSQL version 2 shape does. Leased, queried, and purged envelopes carry the recorded handler full name.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `EfCoreOutboxStore` | Implements all outbox store roles on `DbSet<OutboxMessageEntity>` |
| `ITransactionalOutbox<TContext>` | Transactional enqueue with a typed context |
| `LiteBusOutboxSaveChangesInterceptor` | Stages enqueues until `SaveChanges` |
| `OutboxMessageEntity` | Persistence entity for outbox rows |
| `IOutboxDbContext` | DbContext marker interface |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IOutboxStore.EnqueueAsync` | Auto-commit operation context (calls `SaveChanges`) |
| `ITransactionalOutbox<TContext>.EnqueueAsync` | Command handlers running in an EF transaction |
| `EfCoreOutboxStore.UseExistingDbContext` | Callers staging on their own context |
| `IOutboxLeaseStore.LeasePendingAsync` | Outbox processor |

### Registration

```csharp
outbox.UseEntityFrameworkCoreStorage(o =>
{
    o.UseDbContext<AppDbContext>();
    o.EnableSaveChangesInterceptor();
});
```

- Register the interceptor on both the LiteBus module and `DbContextOptions`.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `EntityFrameworkCoreOutboxStoreOptions` | Sets schema, table, and lease duration |
| `UseDbContext<TContext>()` | Selects the application-registered `IDbContextFactory<TContext>` |
| `EnableSaveChangesInterceptor()` / `EnforceTransactionalSetup()` | Enables transactional enqueue |
| `GetModelBuilderConfiguration()` | Configures the application-owned EF model |

### Extension Points

- The store supports the relational providers named by `EfCoreStorageProvider`.
- `UseExistingDbContext` supports read/write splits and manual unit-of-work patterns.

## Packages

| Package | Dependency role |
| --- | --- |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | Feature bridge |
| `LiteBus.Storage.EntityFrameworkCore` | Technology adapter (transitive) |

## Requires

- `IOutboxDbContext` is required on the application `DbContext`
- The application composition registers `AddDbContextFactory<TContext>(...)`
- The application owns its EF migrations
- A registered dispatcher, when the processor is enabled

## Invariants

- Schema and table names in options must match `OnModelCreating` configuration.
- The interceptor pattern requires registration on both the LiteBus module and the `DbContext` options.
- A null filter or lease tenant means no tenant restriction. An empty string matches only unscoped rows.
- Strict batches pre-check known duplicates and throw `IdempotencyConflictException` for conflicting content. Only concurrent races reach the database unique index. Single-add strict checks apply to rows staged on the same context as well.
- After a unique violation, the store reinserts rows that are still missing, and chains the database error when the retry also fails.
- `EnableSaveChangesInterceptor()` registers both `ITransactionalOutbox<TContext>` and the non-generic `ITransactionalOutbox` adapter, so collected domain events stage into the ambient EF transaction.
- For renew, release, and terminal persist on SQLite and the in-memory provider, the store compares `lease_generation` through an Entity Framework concurrency token. When a worker loses the race, the store reports skipped instead of overwriting the new holder.
- Strict comparison includes the `handler` targeting alongside identity, contract, payload, topic, key, correlation, causation, and tenant.
- Leased envelopes carry the stored `IdempotencyKey` on every EF relational provider, so key readers and FIFO deduplication mapping see the key.

## Non-Goals

- EF migrations driven by LiteBus
- Publishing to a broker

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

Register them through `AddLiteBusOutboxMetrics()`. The instrument names are constants on `LiteBusOutboxTelemetry`.

## Further Reading

- [Outbox Entity Framework Core storage](../../integrations/outbox-ef-core-storage.md)
- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_ShouldRegisterSingleStoreInstanceForAllRoles`

- **Use case**: Register single outbox store instance for all roles
- **Test kind**: Unit
- **Description**: Builds the EF outbox storage module
- **Behavior**: Resolves all outbox store interfaces
- **Expected outcome**: Every role resolves to the same instance
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStorageModuleTests.AddEfCoreOutboxStorage_WithEnforceTransactionalSetupWithoutInterceptor_ShouldThrowOnBuild`

- **Use case**: Throw when transactional setup enforced without interceptor
- **Test kind**: Unit
- **Description**: Enforces transactional setup without the interceptor
- **Behavior**: Builds the module
- **Expected outcome**: The build throws a configuration exception
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: In-memory EF outbox contract suite
- **Test kind**: Contract (unit)
- **Description**: Runs the full contract against the EF in-memory provider
- **Behavior**: Runs the inherited contract tests
- **Expected outcome**: Results match the shared contract suite
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStorePostgreSqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: PostgreSQL EF outbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Runs the migrated PostgreSQL EF store contract tests
- **Behavior**: Runs the inherited contract tests
- **Expected outcome**: Results match the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStoreSqlServerContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: SQL Server EF outbox contract suite
- **Test kind**: Contract (integration)
- **Description**: Runs the migrated SQL Server EF store contract tests
- **Behavior**: Runs the inherited contract tests
- **Expected outcome**: Results match the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxProcessorEndToEndTests.ProcessPendingAsync_ShouldPublishEventThroughEfCoreStore`

- **Use case**: Processor E2E publishes through EF outbox store
- **Test kind**: Integration
- **Description**: Enqueues and processes through the EF store
- **Behavior**: Runs the processor with the in-process dispatcher
- **Expected outcome**: The event is published and the row is terminal
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxFindExistingTests.EnqueueAsync_when_id_and_idempotency_key_match_different_rows_should_prefer_message_id_row`

- **Use case**: Prefer message id row when id and idempotency match different rows
- **Test kind**: Unit
- **Description**: Seeds two rows with overlapping identifiers
- **Behavior**: Enqueues while resolving the existing row
- **Expected outcome**: The message id match wins over the idempotency key
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreConcurrencyTests.PersistAsync_WhenTwoWorkersPersistSameEnvelopeConcurrently_ShouldApplyExactlyOnce`

- **Use case**: Concurrent persist applies exactly once
- **Test kind**: Integration
- **Description**: Two workers persist the same leased envelope
- **Behavior**: Runs `PersistAsync` concurrently
- **Expected outcome**: One terminal update is applied
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapVersion1Columns`

- **Use case**: Map version 1 outbox columns in model
- **Test kind**: Unit
- **Description**: Configures the model extension
- **Behavior**: Inspects the EF model
- **Expected outcome**: The v1 column layout is mapped
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `OutboxEntityFrameworkCoreModelTests.GetModelBuilderConfiguration_ShouldMapEntityColumns`

- **Use case**: Map outbox entity columns in model
- **Test kind**: Unit
- **Description**: Maps properties and columns
- **Behavior**: Runs the model builder extension
- **Expected outcome**: The topic, payload, and scheduling columns are mapped
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxSaveChangesInterceptorIntegrationTests.SaveChangesAsync_ShouldPersistOutboxMessage_WhenTransactionCommits`

- **Use case**: Interceptor persists outbox on commit
- **Test kind**: Integration
- **Description**: Enqueues through the interceptor and commits
- **Behavior**: Completes `SaveChanges` and commit successfully
- **Expected outcome**: The outbox row is in the database
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `TransactionalOutboxEnqueueTests.EnqueueAsync_should_stage_envelope_with_contract_and_metadata`

- **Use case**: Transactional enqueue stages envelope with metadata
- **Test kind**: Unit
- **Description**: Uses `ITransactionalOutbox` before save
- **Behavior**: Enqueues, then inspects the staged state
- **Expected outcome**: The contract and metadata are staged
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldCommitDomainAndOutboxTogether`

- **Use case**: EF use-existing DbContext commits outbox with domain
- **Test kind**: Integration
- **Description**: Uses a shared DbContext transaction
- **Behavior**: Runs SaveChanges and commits
- **Expected outcome**: The domain and outbox rows are committed
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStoreMySqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: MySQL EF outbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Runs the full outbox contract suite against MySQL 8.4 through Pomelo
- **Behavior**: Covers ordered concurrent leasing, append outcomes, fencing, queries, and purge
- **Expected outcome**: Outcomes match the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStoreSqliteContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: File-backed SQLite outbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Runs the full outbox contract suite against independent contexts sharing one database file
- **Behavior**: Covers serializable claims, UTC-tick timestamp queries, append outcomes, and fencing
- **Expected outcome**: Outcomes match the shared contract suite
- **Remarks**: `LiteBus.Storage.IntegrationTests`

### Untested

- No integration test covers a read/write split DbContext with explicit `UseExistingDbContext`.

### Out-of-Scope

- EF migrations driven by LiteBus.
- Publishing to a broker (dispatch axis).
