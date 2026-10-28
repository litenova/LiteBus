# State Writer Store Role

- **ID**: `storage.role.state-writer`
- **Summary**: Persists post-dispatch outcomes for leased inbox and outbox envelopes.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxStateWriter` | `PersistAsync(IReadOnlyList<InboxEnvelope>)` |
| `IOutboxStateWriter` | `PersistAsync(IReadOnlyList<OutboxEnvelope>)` |

`PersistAsync` returns `PersistResult` with applied and skipped counts. The skipped count covers updates dropped because another worker holds the lease.

## SQL Behavior

- The persist path updates status and timestamps based on the envelope terminal state.
- Inbox success writes `completed_at`. Outbox success writes `published_at`.
- The retry path sets failed status and the next `visible_after`.
- The dead-letter path sets dead-letter status and the dead-letter timestamp where the schema supports it.
- Persist validates statuses up front on every adapter and throws `InvalidOperationException` for non-terminal envelopes instead of overwriting rows.

## Concurrency Model

- Persist updates include lease ownership checks.
- If lease ownership was lost, the update is skipped and reported in `PersistResult`.
- This prevents one worker from overwriting terminal state written by another worker.

## Index Interaction

- State updates target the primary key `message_id`.
- The lease index still matters, because retry states move rows back into leasable scans.

## Observability

- State outcomes drive the processor counters. The outcome counters are internal, while `persist_skipped` and `persist_rejected` are public constants:
  - inbox: `litebus.inbox.processor.succeeded`, `litebus.inbox.processor.failed`, `litebus.inbox.processor.dead_lettered`, `litebus.inbox.processor.persist_skipped`, `litebus.inbox.processor.persist_rejected`
  - outbox: `litebus.outbox.processor.published`, `litebus.outbox.processor.failed`, `litebus.outbox.processor.dead_lettered`, `litebus.outbox.processor.persist_skipped`, `litebus.outbox.processor.persist_rejected`
- Queue-depth gauges reflect status movement between active and terminal states.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests cover failed-state persistence and retry visibility scheduling.
- EF unit suites cover transactional staging and field mapping before persistence.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStorePersistResultTests` covers completed timestamps and lost-lease reporting.
- `PostgreSqlInboxStoreBatchMarkFailedTests` covers batch failed persistence behavior.
- End-to-end inbox and outbox processor tests cover success, retry, and dead-letter terminal writes.
- The EF integration suites in the same project verify parity for failure and dead-letter state transitions.

## Concrete Example

A handler fails with a retry delay of 30 seconds. The state writer marks the row failed and sets `visible_after` to now plus 30 seconds, so the lease role will skip it until the delay passes.

## Related Docs

- [storage.role.lease](storage.role.lease.md)
- [storage.role.dead-letter](storage.role.dead-letter.md)
