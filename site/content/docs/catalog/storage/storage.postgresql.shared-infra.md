# PostgreSQL Shared Infrastructure

- **ID**: `storage.postgresql.shared-infra`
- **Package**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: Shared primitives that the PostgreSQL inbox and outbox adapters use for schema lifecycle, identifier safety, locking, work signaling, and transactional participation.

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

- The infrastructure stores schema metadata in `litebus_schema_versions`.
- It renders table and index names from SQL resource templates and tokens.
- It derives advisory lock key pairs from a stable hash of the lock name.
- It supports schema drift checks by comparing expected columns and required index names.

## Concurrency Model

- PostgreSQL advisory locks serialize schema bootstrap.
- The work signal keeps a dedicated `LISTEN` connection and reconnects after failures.
- Transactional participants read the scoped ambient transaction context that application code provides.

## Index and Naming Helpers

- `PostgreSqlIdentifier.IndexName` generates index names with length-safe trimming.
- The inbox and outbox adapters provide their required index name sets to the schema inspector.
- Quoting and qualification are centralized, so SQL identifiers stay consistent.

## Observability

- No meter is emitted directly by `LiteBus.Storage.PostgreSql`.
- The inbox and outbox axis telemetry meters expose the queue depth gauges.
- The infrastructure reports schema drift details by raising `PostgreSqlSchemaDriftException`, which the adapter schema checks then surface.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTableReferenceTests` covers schema and table mapping and validation.
- `PostgreSqlSchemaInspectorTests` covers version inference and missing-column detection.
- `PostgreSqlTransactionalParticipantTests` covers write mode behavior when the ambient transaction is missing.
- `PostgreSqlStorageExceptionTests` covers the configuration and timeout exception contracts.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlIdentifierTests` covers quoting, qualification, stable hashing, and index naming.
- `PostgreSqlAdvisoryLockScopeTests` covers deterministic lock-key derivation and key separation.
- `PostgreSqlStorageUtilityTests` covers the data source factory and canonical SQL path checks.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Dependency Graph](../../architecture/dependency-graph.md)
