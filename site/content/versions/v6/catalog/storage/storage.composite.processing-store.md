# Processing Composite Store

- **ID**: `storage.composite.processing-store`
- **Summary**: Composite role that combines lease and state-writer responsibilities for processor loops.

## Interface Methods

| Composite | Extended interfaces | Methods involved |
| --- | --- | --- |
| `IInboxProcessingStore` | `IInboxLeaseStore`, `IInboxStateWriter` | `LeasePendingAsync`, `RenewLeaseAsync`, `PersistAsync` |
| `IOutboxProcessingStore` | `IOutboxLeaseStore`, `IOutboxStateWriter` | `LeasePendingAsync`, `RenewLeaseAsync`, `PersistAsync` |

Acceptance methods (`AddAsync`, `AddBatchAsync`) remain on append interfaces and are not part of processing composites.

## SQL Behavior

- Composite methods execute against the same physical table as append and operations roles.
- Lease path reads due rows and marks owner plus expiry.
- State path updates status and timestamps with lease guard checks.

## Concurrency Model

- Composite usage keeps lease and state transitions in one store implementation, which avoids cross-store race windows.
- PostgreSQL adapters use `FOR UPDATE SKIP LOCKED` for disjoint claims.
- Persist operations return applied and skipped counts for lost-lease detection.

## Index Interaction

- Lease scan index is required for claim operations.
- Idempotency and topic indexes are not directly used by processing methods but remain part of the same table definition.

## Observability

- Composite operations drive processor metrics:
  - `...processor.leases_acquired`
  - `...processor.succeeded` or `...processor.published`
  - `...processor.failed`
  - `...processor.dead_lettered`
  - `...processor.persist_skipped`
  - `...processor.persist_rejected`
- Queue-depth gauges reflect status movement across pending, processing/publishing, and terminal states.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxEndToEndTests` and `PostgreSqlOutboxEndToEndTests`: lease plus persist path.
- `PostgreSqlInboxProcessorLeaseStressTests`: concurrent processor claims and single terminal outcome.
- `PostgreSqlModuleRegistrationTests`: role interfaces resolve to one singleton store instance.

### `LiteBus.Storage.UnitTests`

- Module dependency tests validate that storage cannot build without its parent durable module.
- EF and InMemory module tests in same project verify one-instance role mapping per axis adapter.

## Related Docs

- [Architecture](../../architecture/README.md#composite-store-roles)
- [storage.role.lease](storage.role.lease.md)
- [storage.role.state-writer](storage.role.state-writer.md)
