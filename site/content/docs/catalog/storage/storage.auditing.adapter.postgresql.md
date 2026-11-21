# Auditing PostgreSQL Adapter

- **ID**: `storage.auditing.adapter.postgresql`
- **Package**: `LiteBus.Auditing.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A scoped `PostgreSqlAuditTrail<TDocument>` stages audit records in the ambient transaction, and `PostgreSqlAuditChainStore<TDocument>` implements the chain store port on three Npgsql tables.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IAuditTrail` | `WriteAsync` |
| `IAuditChainStore<TDocument>` | `ReadHeadAsync`, `AppendAsync`, `StageAsync`, `ListUnlinkedAsync`, `ReadSegmentAsync`, `ListHeadsAsync`, `StoreCheckpointAsync`, `ReadCheckpointAsync`, `ListUnpublishedAsync`, `MarkPublishedAsync`, `EraseAsync` |
| `ICheckpointSource` | `GetLatestAsync` |

## PostgreSQL Behavior

- Register with `auditing.UsePostgreSqlStore()` inside `AddAuditing` plus a `PostgreSqlAuditModule` carrying the data source and the table names. The module registers the data source, the table options, the default document canonicalizer, the schema bootstrap, and the schema probe. The auditing module closes the open-generic trail and chain store over the configured document type.
- A record for a successful mediation is staged through the ambient `IPostgreSqlTransactionProvider` transaction when one is active. A record for a denial, failure, cancellation, or invalid message is staged out of band on its own connection and committed immediately. Without a registered transaction provider, every record commits on its own connection.
- The default document is `PostgreSqlAuditRecord`, mapped by `PostgreSqlAuditRecordMapper` and segmented by `PostgreSqlAuditRecordSegmenter` into `{category}/{year}` with `uncategorized` for a record that carries no category. The segmenter describes each segment with the record tenant and the `category` and `year` attributes.
- The heads table holds one row per segment with the conditional-write version. Every linked append asserts the expected head version and throws `AuditHeadConflictException` on mismatch, and duplicate sequences are backed by a unique constraint that surfaces the same way.
- Stored checkpoints carry their publish state. The chain store reads them back as the first verification source.
- An explicit `StoreAs` or `SegmentBy` call on the chain builder always wins over the store defaults, regardless of call order.

## Schema Versions

- The audit schema uses version 1. Its records table holds staged and linked documents, its heads table holds one row per chain segment, and its checkpoints table holds stored checkpoints with their publish state. The tables default to `public.litebus_audit_records`, `public.litebus_audit_heads`, and `public.litebus_audit_checkpoints`.
- `EnsureAsync` creates the schema idempotently under a PostgreSQL advisory lock, so several application instances can run it at once. `ValidateAsync` fails startup with `PostgreSqlSchemaDriftException` when a table is missing, incomplete, or recorded at an unexpected version.
- Migration-owned deployments copy the canonical SQL files from `PostgreSqlAuditSchema.SqlFiles` or render the script from `PostgreSqlAuditSchema.GetCreateScript`. See `SqlFiles` on `PostgreSqlAuditSchema`.

## Concurrency Model

- The head version check serializes linked appends per segment. A concurrent append fails and the linker retries against the winner, so two records never share a sequence number.
- Unlinked reads return staged documents in commit order, so the linker assigns positions in the order records committed.

## Observability

- The schema probe is `audit.postgresql.schema`.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlAuditChainStoreTests` inherits the shared `AuditChainStoreContractTests` suite and covers heads, conditional appends, staging, unlinked reads, segment reads, checkpoints, publish state, and erasure.
- `PostgreSqlAuditTrailTests` covers staging successes in the ambient transaction and writing refusals and faults out of band.
- `PostgreSqlAuditSchemaTests` covers schema creation, validation, and drift reporting.
- `PostgreSqlAuditStoreBuilderTests` covers module registration and store defaults.

## Related Docs

- [PostgreSQL Audit Storage](../../integrations/auditing-postgresql.md)
- [Auditing](../../concepts/auditing.md)
- [storage.auditing.adapter.marten](storage.auditing.adapter.marten.md)
- [storage.auditing.adapter.efcore](storage.auditing.adapter.efcore.md)
- [storage.postgresql.schema-management](storage.postgresql.schema-management.md)
