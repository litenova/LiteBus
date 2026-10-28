# Lease Store Role

- **ID**: `storage.role.lease`
- **Summary**: Claims due rows for processor workers and tracks lease ownership.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` |
| `IOutboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` |
| `ILeaseRenewable` | Renewal and release contract, where `RenewLeaseAsync` extends a lease and `ReleaseLeaseAsync` returns a cancelled lease without charging the attempt. The default implementation returns `false` |

## SQL Behavior

- The PostgreSQL claim query uses `FOR UPDATE SKIP LOCKED` with due-time and lease-expiry predicates.
- The claim update sets `status`, `lease_owner`, `lease_expires_at`, and increments `attempt_count`.
- Eligible statuses include pending and failed. Expired processing and publishing leases are reclaimable.
- A null lease tenant means no tenant restriction. An empty string leases only unscoped rows. Stored and requested tenants are compared through normalized values on every adapter.

## Concurrency Model

- Multiple workers can lease in parallel without double-claiming the same row.
- When a worker is lost, lease expiry and reclaim handle its rows.
- The renewal path extends `lease_expires_at` when the caller still owns the lease.
- The release path returns a dispatch the processor cancelled to pending (or to failed when the row already carries an error) and decrements the attempt count. A store that does not implement release keeps the default, so the lease expires with the attempt counted.

## Index Interaction

- The lease index `(status, visible_after, lease_expires_at, created_at)` is required for scalable claim scans.
- FIFO-like processing order depends on `created_at` ordering after due-time filtering.

## Observability

- Lease outcomes feed processor counters:
  - `litebus.inbox.processor.leases_acquired`
  - `litebus.outbox.processor.leases_acquired`
  - `litebus.inbox.processor.lease_lost`
  - `litebus.outbox.processor.lease_lost`
- Queue-depth gauges reflect rows moving between pending and processing or publishing states.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests verify ordering, batch-size limits, deferred visibility handling, and reclaim behavior.
- InMemory lease availability tests validate edge cases with missing lease expiry values.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- PostgreSQL contract suites validate disjoint leasing under concurrency.
- `PostgreSqlInboxProcessorLeaseStressTests` validates one terminal state under parallel workers.
- End-to-end inbox and outbox processor tests validate the workflow of leasing plus dispatch and publish.

## Concrete Example

Two processor instances start at the same time with batch size 10. PostgreSQL lease queries skip rows already locked by the first worker, so each worker gets a separate subset.

## Related Docs

- [storage.composite.processing-store](storage.composite.processing-store.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
