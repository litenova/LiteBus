# Auditing Marten Adapter

- **ID**: `storage.auditing.adapter.marten`
- **Package**: `LiteBus.Auditing.Storage.Marten` (feature bridge)
- **Shared dependency**: None. Marten is referenced directly, and the application registers it with `AddMarten`
- **Summary**: A scoped `MartenAuditTrail<TDocument>` stages audit records on the Marten session, and `MartenAuditChainStore<TDocument>` implements the chain store port on Marten documents.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IAuditTrail` | `WriteAsync` |
| `IAuditChainStore<TDocument>` | `ReadHeadAsync`, `AppendAsync`, `StageAsync`, `ListUnlinkedAsync`, `ReadSegmentAsync`, `ListHeadsAsync`, `StoreCheckpointAsync`, `ReadCheckpointAsync`, `ListUnpublishedAsync`, `MarkPublishedAsync`, `EraseAsync` |
| `ICheckpointSource` | `GetLatestAsync` |

## Marten Behavior

- Register with `auditing.UseMartenStore()` inside `AddAuditing`. LiteBus does not register Marten. The application calls `AddMarten` against the database the audit documents live in, and the trail and the chain store resolve the scoped `IDocumentSession` and the `IDocumentStore` from the container.
- A record for a successful mediation is staged on the scoped session and committed by the same `SaveChangesAsync` that commits the handler change. A record for a denial, failure, cancellation, or invalid message is staged out of band on a short session and saved immediately.
- The default document is `MartenAuditRecord`, mapped by `MartenAuditRecordMapper` and segmented by `MartenAuditRecordSegmenter` into `{category}/{year}` with `uncategorized` for a record that carries no category. The segmenter describes each segment with the record tenant and the `category` and `year` attributes.
- A `MartenAuditSegmentHead` document per segment carries the last sequence number and hash, the document count, the last checkpoint, and the conditional-write version. Every linked append asserts the expected head version and throws `AuditHeadConflictException` on mismatch, and duplicate sequences are backed by a unique constraint that surfaces the same way.
- Stored checkpoints are `MartenAuditCheckpoint` documents with their publish state. The chain store reads them back as the first verification source.
- An explicit `StoreAs` or `SegmentBy` call on the chain builder always wins over the store defaults, regardless of call order.

## Concurrency Model

- The head version check serializes linked appends per segment. A concurrent append fails its commit and the linker retries against the winner, so two records never share a sequence number.
- Unlinked reads return staged documents in commit order through `MartenAuditUnlinkedEntry` ordering, so the linker assigns positions in the order records committed.

## Observability

- There is no diagnostic probe. Marten owns its document schema, and the audit tables need no version tracking beyond the head each segment carries.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`Marten/`)

- `MartenAuditChainStoreTests` inherits the shared `AuditChainStoreContractTests` suite and covers heads, conditional appends, staging, unlinked reads, segment reads, checkpoints, publish state, and erasure.
- `MartenAuditStoreTrailTests` covers staging successes on the scoped session and writing refusals and faults out of band.
- `MartenHashChainAuditTrailTests` covers the chained trail end to end on Marten.
- `MartenAuditChainTests` covers chain operations through the store.

### `LiteBus.Storage.UnitTests` (`Marten/`)

- `MartenAuditStoreDefaultsTests` covers the default document, mapper, and segmenter the store extension records.
- `MartenAuditHashChainTests` covers scheme and mapping defaults without a database.

## Related Docs

- [Marten Audit Storage](../../integrations/auditing-marten.md)
- [Auditing](../../concepts/auditing.md)
- [storage.auditing.adapter.postgresql](storage.auditing.adapter.postgresql.md)
- [storage.auditing.adapter.efcore](storage.auditing.adapter.efcore.md)
