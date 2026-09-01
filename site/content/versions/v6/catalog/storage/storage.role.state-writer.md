# State Writer Store Role

- **ID**: `storage.role.state-writer`
- **Summary**: Persists post-dispatch outcomes for leased inbox and outbox envelopes.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxStateWriter` | `PersistAsync(IReadOnlyList<InboxEnvelope>)` |
| `IOutboxStateWriter` | `PersistAsync(IReadOnlyList<OutboxEnvelope>)` |

`PersistAsync` returns `PersistResult` with applied and skipped counts for lease-guard behavior.

## SQL Behavior

- Persist path updates status and timestamps based on envelope terminal state.
- Inbox success writes `completed_at`; outbox success writes `published_at`.
- Retry path sets failed status and next `visible_after`.
- Dead-letter path sets dead-letter status and dead-letter timestamp where schema supports it.

## Concurrency Model

- Persist updates include lease ownership checks.
- If lease ownership was lost, update is skipped and reported in `PersistResult`.
- This prevents one worker from overwriting terminal state written by another worker.

## Index Interaction

- State updates are primary-key based by `message_id`.
- Lease index still matters because retry states move rows back into leasable scans.

## Observability

- State outcomes drive processor counters:
  - inbox: `succeeded`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`
  - outbox: `published`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`
- Queue-depth gauges reflect status movement between active and terminal states.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests cover failed-state persistence and retry visibility scheduling.
- EF unit suites cover transactional staging and field mapping before persistence.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStorePersistResultTests`: completed timestamps and lost-lease reporting.
- `PostgreSqlInboxStoreBatchMarkFailedTests`: batch failed persistence behavior.
- End-to-end inbox and outbox processor tests: success, retry, and dead-letter terminal writes.
- EF integration suites in same project verify parity for failure and dead-letter state transitions.

## Concrete Example

A handler fails with a retry delay of 30 seconds. The state writer marks the row failed and sets `visible_after` to now plus 30 seconds, so the lease role will skip it until the delay passes.

## Related Docs

- [storage.role.lease](storage.role.lease.md)
- [storage.role.dead-letter](storage.role.dead-letter.md)
