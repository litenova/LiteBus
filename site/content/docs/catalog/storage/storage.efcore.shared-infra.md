# EF Core Shared Infrastructure

- **ID**: `storage.efcore.shared-infra`
- **Name**: EF Core shared infrastructure
- **Maturity**: GA
- **Summary**: Provider-specific lease SQL, bulk update helpers, and relational model utilities for EF-backed durable stores.

## What It Does

`LiteBus.Storage.EntityFrameworkCore` supplies cross-axis EF primitives: provider detection (`EfCoreStorageProvider`), provider-specific lease SQL (PostgreSQL `FOR UPDATE SKIP LOCKED`, SQL Server, MySQL/Pomelo), serializable SQLite lease transactions, SQLite UTC-tick timestamp mapping, relational table qualification, JSON text normalization, bulk update capability probes, and shared durable store operation helpers. Inbox and outbox EF adapters use these types internally; applications typically interact through axis-specific model extensions and store options.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `EfCoreStorageProvider` | PostgreSQL, SQL Server, MySQL, SQLite, InMemory detection |
| `EfCoreRelationalLeaseExecutor` | Internal: executes provider lease SQL against a `DbContext` |
| `EfCorePostgreSqlLeaseSql` / `EfCoreSqlServerLeaseSql` / `EfCoreMySqlLeaseSql` | Internal lease statement templates |
| `EfCoreDurableStoreOperations` | Internal shared state transition helpers |
| `EfCoreBulkUpdateCapabilities` | Internal provider bulk update feature detection |
| `EfCoreRelationalModelColumnTypes` | Payload and trace column type mapping |
| `EfCoreJsonTextNormalizer` | Internal JSON serialization alignment for relational providers |
| `EfCoreProviderResolver` | Resolve provider from `DbContext` |

### Invocation

| API | Typical caller |
| --- | --- |
| `EfCoreRelationalLeaseExecutor` | `EfCoreInboxStore` / `EfCoreOutboxStore` lease paths |
| `EfCoreDurableStoreOperations` | Persist, mark failed, dead-letter transitions |
| `EfCoreRelationalModelColumnTypes` | Model configuration extensions |
| `EfCoreProviderResolver` | Store construction and lease SQL selection |

### Registration

- Not registered directly; consumed by inbox/outbox EF storage feature bridges.
- Provider detection runs against an operation context created by the application's `IDbContextFactory<TContext>`.

### Configuration

| Option surface | Purpose |
| --- | --- |
| EF provider package | Determines lease SQL and column types (Npgsql, SQL Server, Pomelo) |
| Store options `SchemaName` / `TableName` | Must match fluent model configuration |
| Provider hint on model extensions | Selects text storage for payload and provider-specific storage for trace context |

### Extension Points

- Internal lease and persist helpers are shared with the inbox and outbox EF adapters through `InternalsVisibleTo`; custom adapters build on the public provider resolver, column types, and table qualifier.
- Unsupported providers throw `EfCoreStorageNotSupportedException` at resolve time.

## Packages

| Package | Dependency role |
| --- | --- |
| `LiteBus.Storage.EntityFrameworkCore` | Technology adapter |

## Requires

- EF Core relational provider package used by the application (`Npgsql.EntityFrameworkCore.PostgreSQL`, SQL Server, Pomelo, etc.)
- Axis EF storage adapter (`LiteBus.Inbox.Storage.EntityFrameworkCore` or `LiteBus.Outbox.Storage.EntityFrameworkCore`)

## Invariants

- Lease SQL requires a supported relational provider. SQLite leasing runs inside a serializable transaction, which serializes claims across store instances through the database write lock.
- MySQL leasing uses `READ COMMITTED`, the axis `IX_LiteBus_*_CreatedAt` index, and direct primary-key updates so concurrent workers claim disjoint ordered batches without range-lock starvation.
- Store options `SchemaName` and `TableName` must match the EF model configuration used in migrations.
- Payload columns map to text so payload protection can return opaque ciphertext; trace context uses provider-specific JSON types (`jsonb` on PostgreSQL).
- Table qualification escapes provider quote characters per provider (doubled double-quote for PostgreSQL and SQLite, doubled closing bracket for SQL Server, doubled backtick for MySQL) and rejects null characters.
- Tracked renew and release on providers without bulk update support map a lease-generation concurrency conflict to not-updated, matching the guarded bulk update path. Tracked terminal persist saves one row at a time, so one lost race skips only that row.

## Non-Goals

- Does not register `DbContext` or run migrations.
- Does not replace raw Npgsql inbox/outbox stores for apps that prefer ADO.NET-only persistence.
- Does not implement saga EF storage (not shipped).

## Observability

No dedicated meters in this package. Processor and queue metrics come from axis packages when a processor is enabled.

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.inbox.status` | EF inbox store implements diagnostics role |
| `LiteBus.Inbox` | `litebus.inbox.processor.leases_acquired` | Counter | Each processor pass | none | Lease SQL from this package acquires rows |
| `LiteBus.Inbox` | `litebus.inbox.processor.persist_skipped` | Counter | Terminal persist with lost lease | none | `EfCoreDurableStoreOperations` skip path |
| `LiteBus.Inbox` | `litebus.inbox.processor.persist_rejected` | Counter | Competing persist rejected | none | Optimistic lease guard |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.outbox.status` | EF outbox store implements diagnostics role |
| `LiteBus.Outbox` | `litebus.outbox.processor.leases_acquired` | Counter | Each processor pass | none | Lease SQL from this package |
| `LiteBus.Outbox` | `litebus.outbox.processor.persist_skipped` | Counter | Terminal persist with lost lease | none | Shared persist helpers |
| `LiteBus.Outbox` | `litebus.outbox.processor.persist_rejected` | Counter | Competing persist rejected | none | Optimistic lease guard |

Instrument names are public constants on `LiteBusInboxTelemetry` and `LiteBusOutboxTelemetry`.

## Deep Docs

- [Inbox EF Core storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md)

## Test Coverage

### Covered

#### `EfCoreStorageExceptionTests.EfCoreProviderResolver_throws_EfCoreStorageNotSupportedException_for_unknown_provider`

- **Use case**: Reject unknown EF provider in resolver
- **Test kind**: Unit
- **Description**: DbContext with unsupported provider
- **Behavior**: `EfCoreProviderResolver` resolve
- **Expected outcome**: `EfCoreStorageNotSupportedException` thrown
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreInboxStorePostgreSqlContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: PostgreSQL EF inbox lease and append contract parity
- **Test kind**: Contract (integration)
- **Description**: Full inbox contract suite against EF Npgsql store
- **Behavior**: All inherited contract tests run on PostgreSQL EF
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreInboxStoreSqlServerContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: SQL Server EF inbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Full inbox contract suite against EF SQL Server store
- **Behavior**: Inherited contract tests on SQL Server
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStorePostgreSqlContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: PostgreSQL EF outbox lease and append contract parity
- **Test kind**: Contract (integration)
- **Description**: Full outbox contract suite against EF Npgsql store
- **Behavior**: Inherited contract tests on PostgreSQL EF
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreOutboxStoreSqlServerContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: SQL Server EF outbox contract parity
- **Test kind**: Contract (integration)
- **Description**: Full outbox contract suite against EF SQL Server store
- **Behavior**: Inherited contract tests on SQL Server
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.IntegrationTests`

#### `EfCoreInboxStoreContractTests` (Inherits `InboxStoreContractTests`)

- **Use case**: In-memory EF inbox contract parity
- **Test kind**: Contract (unit)
- **Description**: Full inbox contract suite against EF in-memory provider
- **Behavior**: Inherited contract tests
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreContractTests` (Inherits `OutboxStoreContractTests`)

- **Use case**: In-memory EF outbox contract parity
- **Test kind**: Contract (unit)
- **Description**: Full outbox contract suite against EF in-memory provider
- **Behavior**: Inherited contract tests
- **Expected outcome**: Same outcomes as abstract contract tests
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreInboxStoreParityTests.LeasePendingAsync_FiltersByTenantId`

- **Use case**: Lease filters by tenant id (inbox)
- **Test kind**: Unit
- **Description**: Pending rows for multiple tenants
- **Behavior**: `LeasePendingAsync` with tenant filter
- **Expected outcome**: Only matching tenant rows leased
- **Remarks**: Exercises shared lease executor; `LiteBus.Storage.UnitTests`

#### `EfCoreInboxStoreParityTests.LeasePendingAsync_ReclaimsStaleNullLeaseAfterLeaseDuration`

- **Use case**: Reclaim stale null lease after duration (inbox)
- **Test kind**: Unit
- **Description**: Row with expired null lease owner
- **Behavior**: Lease after lease duration elapsed
- **Expected outcome**: Row reclaimed
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreInboxStoreParityTests.AddBatchAsync_DeduplicatesWithinBatchByIdAndIdempotencyKey`

- **Use case**: Batch deduplication by id and idempotency key (inbox)
- **Test kind**: Unit
- **Description**: Batch with duplicate ids and idempotency keys
- **Behavior**: `AddBatchAsync`
- **Expected outcome**: Within-batch dedup applied
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreInboxStoreParityTests.PersistAsync_ReturnsSkippedCountWhenLeaseIsLost`

- **Use case**: Persist reports skipped count when lease lost (inbox)
- **Test kind**: Unit
- **Description**: Persist after lease stolen by another worker
- **Behavior**: `PersistAsync` with lost lease token
- **Expected outcome**: Skipped count reported; row unchanged
- **Remarks**: Uses `EfCoreDurableStoreOperations`; `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreParityTests.LeasePendingAsync_FiltersByTenantId`

- **Use case**: Lease filters by tenant id (outbox)
- **Test kind**: Unit
- **Description**: Pending outbox rows for multiple tenants
- **Behavior**: `LeasePendingAsync` with tenant filter
- **Expected outcome**: Only matching tenant rows leased
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreParityTests.LeasePendingAsync_ReclaimsStaleNullLeaseAfterLeaseDuration`

- **Use case**: Reclaim stale null lease after duration (outbox)
- **Test kind**: Unit
- **Description**: Outbox row with expired null lease
- **Behavior**: Lease after duration elapsed
- **Expected outcome**: Row reclaimed
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreParityTests.AddBatchAsync_DeduplicatesWithinBatchByIdAndIdempotencyKey`

- **Use case**: Batch deduplication by id and idempotency key (outbox)
- **Test kind**: Unit
- **Description**: Batch with duplicate ids and keys
- **Behavior**: Batch enqueue
- **Expected outcome**: Within-batch dedup applied
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreParityTests.PersistAsync_ReturnsSkippedCountWhenLeaseIsLost`

- **Use case**: Persist reports skipped count when lease lost (outbox)
- **Test kind**: Unit
- **Description**: Persist after lease lost
- **Behavior**: `PersistAsync` with invalid lease
- **Expected outcome**: Skipped count returned
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `EfCoreOutboxStoreConcurrencyTests.PersistAsync_WhenTwoWorkersPersistSameEnvelopeConcurrently_ShouldApplyExactlyOnce`

- **Use case**: Concurrent persist applies exactly once (outbox)
- **Test kind**: Integration
- **Description**: Two workers persist same leased envelope
- **Behavior**: Concurrent `PersistAsync`
- **Expected outcome**: Exactly one terminal update applied
- **Remarks**: `LiteBus.Storage.IntegrationTests`

### Untested

- Dedicated unit tests per provider lease SQL template file (coverage is indirect through contract and integration suites).
- SQLite throughput under sustained multi-process writes; provider contracts cover concurrent claims across independent contexts on one file.

### Out-of-Scope

- Automatic EF migrations or `EnsureCreated` from shared infrastructure.
- Saga EF storage (not shipped).
- Replacing raw Npgsql inbox/outbox stores for ADO-only deployments.
