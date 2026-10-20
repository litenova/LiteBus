# Lease Store Role

- **ID**: `storage.role.lease`
- **Summary**: Claims due rows for processor workers and tracks lease ownership.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` |
| `IOutboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` |
| `ILeaseRenewable` | renewal and release contract: `RenewLeaseAsync` extends a lease, `ReleaseLeaseAsync` returns a cancelled lease without charging the attempt (default returns `false`) |

## SQL Behavior

- PostgreSQL claim query uses `FOR UPDATE SKIP LOCKED` with due-time and lease-expiry predicates.
- Claim update sets `status`, `lease_owner`, `lease_expires_at`, and increments `attempt_count`.
- Eligible statuses include pending and failed; expired processing/publishing leases are reclaimable.
- A null lease tenant means no tenant restriction; an empty string leases only unscoped rows, with stored and requested tenants compared through normalized values on every adapter.

## Concurrency Model

- Multiple workers can lease in parallel without double-claiming the same row.
- Lost worker case is handled by lease expiry and reclaim.
- Renewal path extends `lease_expires_at` when the caller still owns the lease.
- Release path returns a dispatch the processor cancelled to pending (or to failed when the row already carries an error) with the attempt count decremented; without a store implementation the lease expires with the attempt counted.

## Index Interaction

- Lease index `(status, visible_after, lease_expires_at, created_at)` is required for scalable claim scans.
- FIFO-like processing order depends on `created_at` ordering after due-time filtering.

## Observability

- Lease outcomes feed processor counters:
  - `litebus.inbox.processor.leases_acquired`
  - `litebus.outbox.processor.leases_acquired`
  - `litebus.inbox.processor.lease_lost`
  - `litebus.outbox.processor.lease_lost`
- Queue-depth gauges move rows between pending and processing/publishing states.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests verify ordering, batch-size limits, deferred visibility handling, and reclaim behavior.
- InMemory lease availability tests validate edge cases around missing lease expiry values.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- PostgreSQL contract suites validate disjoint leasing under concurrency.
- `PostgreSqlInboxProcessorLeaseStressTests` validates one terminal state under parallel workers.
- End-to-end inbox and outbox processor tests validate lease plus dispatch/publish workflow.

## Concrete Example

Two processor instances start at the same time with batch size 10. PostgreSQL lease queries skip rows already locked by the first worker, so each worker gets a separate subset.

## Related Docs

- [storage.composite.processing-store](storage.composite.processing-store.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
