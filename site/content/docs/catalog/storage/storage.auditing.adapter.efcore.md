# Auditing Entity Framework Core Adapter

- **ID**: `storage.auditing.adapter.efcore`
- **Package**: `LiteBus.Auditing.Storage.EntityFrameworkCore` (feature bridge)
- **Shared dependency**: EF Core shared utilities in `storage.efcore.shared-infra`
- **Summary**: A scoped `EfCoreAuditTrail<TDocument>` stages audit records on the application `DbContext`, and `EfCoreAuditChainStore<TDocument>` implements the chain store port on EF Core entities.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IAuditTrail` | `WriteAsync` |
| `IAuditChainStore<TDocument>` | `ReadHeadAsync`, `AppendAsync`, `StageAsync`, `ListUnlinkedAsync`, `ReadSegmentAsync`, `ListHeadsAsync`, `StoreCheckpointAsync`, `ReadCheckpointAsync`, `ListUnpublishedAsync`, `MarkPublishedAsync`, `EraseAsync` |
| `ICheckpointSource` | `GetLatestAsync` |

## Entity Framework Core Behavior

- Register with `auditing.UseEntityFrameworkCoreStore<TContext>()` inside `AddAuditing`, where `TContext` derives from `DbContext` and implements `IAuditDbContext`. The application registers its own context and maps the audit tables by calling `GetModelBuilderConfiguration()` from `OnModelCreating`; LiteBus registers neither the context nor the out-of-band factory.
- A record for a successful mediation is staged on the scoped context and committed by the same `SaveChanges` that commits the handler change. A record for a denial, failure, cancellation, or invalid message is staged out of band on an isolated context opened through `IAuditDbContextFactory` and saved immediately. Register `EfCoreAuditDbContextFactory<TContext>` behind an Entity Framework context factory so those contexts can be opened.
- The default document is `EfCoreAuditRecord`, mapped by `EfCoreAuditRecordMapper` and segmented by `EfCoreAuditRecordSegmenter` into `{category}/{year}` with `uncategorized` for a record that carries no category. The segmenter describes each segment with the record tenant and the `category` and `year` attributes.
- `AuditSegmentHeadEntity` carries one row per segment with the conditional-write version. Every linked append asserts the expected head version and throws `AuditHeadConflictException` on mismatch, and duplicate sequences are backed by a unique index that surfaces the same way.
- Stored checkpoints are `AuditCheckpointEntity` rows with their publish state. The chain store reads them back as the first verification source.
- An explicit `StoreAs` or `SegmentBy` call on the chain builder always wins over the store defaults, regardless of call order.

## Default Table and Columns

| Setting | Default |
| --- | --- |
| Schema | `public` |
| Records table | `litebus_audit_records` |
| Segment heads table | `litebus_audit_segment_heads` |
| Checkpoints table | `litebus_audit_checkpoints` |

`GetModelBuilderConfiguration()` maps the segment head and checkpoint tables, the default record table with its chain columns and staged-at ordering, and the segment, sequence, and staged-at index. The index is unique with a `sequence > 0` filter where the provider supports filtered indexes; unlinked documents all carry sequence zero, so the uniqueness covers linked documents only. The generic overload maps a custom document type instead: the application owns the document table name and primary key, and LiteBus configures only the chain columns, the staged-at shadow property, and the index. Pass `EfCoreStorageProvider` so payload and timestamp columns use the correct store types.

## Concurrency Model

- The head version check serializes linked appends per segment. A concurrent append fails and the linker retries against the winner, so two records never share a sequence number.
- Unlinked reads return staged documents in staged-at order, so the linker assigns positions in the order records committed.

## Observability

- There is no diagnostic probe. The application owns migrations, so schema state is visible in the migration history rather than in a LiteBus check.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/`)

- `EfCoreAuditChainStoreTests` inherits the shared `AuditChainStoreContractTests` suite and covers heads, conditional appends, staging, unlinked reads, segment reads, checkpoints, publish state, and erasure.
- `EfCoreAuditChainStorePostgreSqlTests` proves the same contract against PostgreSQL.
- `EfCoreAuditStoreTrailTests` covers staging successes on the scoped context and writing refusals and faults through the out-of-band factory.
- `EfCoreAuditStoreBuilderTests` covers registration and store defaults.

## Related Docs

- [Entity Framework Core Audit Storage](../../integrations/auditing-entity-framework-core.md)
- [Auditing](../../concepts/auditing.md)
- [storage.auditing.adapter.marten](storage.auditing.adapter.marten.md)
- [storage.auditing.adapter.postgresql](storage.auditing.adapter.postgresql.md)
- [storage.efcore.schema-ownership](storage.efcore.schema-ownership.md)
