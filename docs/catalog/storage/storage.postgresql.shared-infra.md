# PostgreSQL Shared Infrastructure

- **ID**: `storage.postgresql.shared-infra`
- **Package**: `LiteBus.Storage.PostgreSql` (layer 3)
- **Summary**: Shared primitives used by PostgreSQL inbox and outbox adapters for schema lifecycle, identifier safety, locking, work signaling, and transactional participation.

## Main Contracts and Methods

| Contract or type | Core methods or behavior |
| --- | --- |
| `IPostgreSqlTransactionProvider` | `TryGetCurrent(out connection, out transaction)` |
| `TransactionalWriteMode` | `RequireActiveTransaction`, `AllowImmediateCommit` |
| `PostgreSqlWorkSignal` | `WaitForWorkOrDelayAsync`, async listener lifecycle |
| `PostgreSqlSchemaInspector` | `TableExistsAsync`, `IndexExistsAsync`, `GetColumnNamesAsync`, `InferVersionFromColumns` |
| `PostgreSqlAdvisoryLockScope` | `TryAcquireAsync`, `AcquireAsync`, `DisposeAsync` unlock |
| `PostgreSqlSchemaVersionStore` | reads and writes `litebus_schema_versions` metadata |
| `PostgreSqlIdentifier` and `PostgreSqlTableReference` | deterministic quoting, naming, and table token mapping |

## SQL and Metadata Behavior

- Stores schema metadata in `litebus_schema_versions`.
- Uses SQL resource templates and token rendering for table and index names.
- Uses advisory lock key pairs derived from a stable hash of lock name.
- Supports schema drift checks by comparing expected columns and required index names.

## Concurrency Model

- Schema bootstrap is serialized by PostgreSQL advisory locks.
- Work signal keeps a dedicated `LISTEN` connection and reconnects after failures.
- Transactional participants read scoped ambient transaction context from application code.

## Index and Naming Helpers

- Index names are generated through `PostgreSqlIdentifier.IndexName` and length-safe trimming.
- Inbox and outbox adapters provide required index name sets to schema inspector.
- Quoting and qualification are centralized to avoid SQL identifier mismatches.

## Observability

- No meter is emitted directly by `LiteBus.Storage.PostgreSql`.
- Queue depth gauges remain exposed by inbox and outbox axis telemetry meters.
- Schema drift details are raised through `PostgreSqlSchemaDriftException`, then surfaced by adapter schema checks.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTableReferenceTests`: schema/table mapping and validation.
- `PostgreSqlSchemaInspectorTests`: version inference and missing-column detection.
- `PostgreSqlTransactionalParticipantTests`: write mode behavior for missing ambient transaction.
- `PostgreSqlStorageExceptionTests`: configuration and timeout exception contracts.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlIdentifierTests`: quoting, qualification, stable hash, index naming.
- `PostgreSqlAdvisoryLockScopeTests`: deterministic lock-key derivation and key separation.
- `PostgreSqlStorageUtilityTests`: data source factory and canonical SQL path checks.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Dependency Graph](../../architecture/dependency-graph.md)
