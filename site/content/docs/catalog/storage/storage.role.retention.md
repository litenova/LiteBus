# Retention Store Role

- **ID**: `storage.role.retention`
- **Summary**: Deletes old terminal rows to control inbox and outbox table growth.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxRetentionStore` | `DeleteCompletedOlderThanAsync(DateTimeOffset olderThan)` |
| `IOutboxRetentionStore` | `DeletePublishedOlderThanAsync(DateTimeOffset olderThan)` |

## SQL Behavior

- The inbox retention filter is `status = completed AND COALESCE(completed_at, created_at) < cutoff`.
- The outbox retention filter is `status = published AND COALESCE(published_at, created_at) < cutoff`.
- Deletes are hard deletes and return the affected row count.

## Concurrency Model

- Retention runs safely alongside processors because it targets terminal statuses only.
- Active rows (pending, processing, publishing, failed, dead-lettered) are excluded by status predicates.

## Index Interaction

- Retention defines no dedicated index in the v1 schema.
- The lease index still supports mixed workloads, because active-row scans and retention deletes run concurrently.

## Observability

- Cleanup error counters:
  - `litebus.inbox.cleanup.errors`
  - `litebus.outbox.cleanup.errors`
- Queue-depth gauges show reduced completed and published counts after cleanup cycles.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- `InboxRetentionStoreContractTests` covers eligible delete, no-op delete, and completion-time precedence.
- `OutboxRetentionStoreContractTests` covers eligible delete, no-op delete, and publication-time precedence.
- InMemory outbox retention tests verify idempotency index entries are also removed.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- PostgreSQL retention contract suites cover inbox and outbox.
- Hosted cleanup integration tests verify periodic purge through the cleanup background service.
- EF PostgreSQL and SQL Server retention contract suites verify cross-provider parity.

## Concrete Example

If a row was created yesterday but completed five minutes ago, retention with a one-hour cutoff keeps it. The cutoff uses completion time, not creation time.

## Related Docs

- [Architecture](../../architecture/README.md#retention-cutoff-semantics)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
