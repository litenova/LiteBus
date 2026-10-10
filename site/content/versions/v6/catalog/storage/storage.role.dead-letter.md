# Dead-Letter Store Role

- **ID**: `storage.role.dead-letter`
- **Summary**: Requeues dead-lettered rows for operator-driven replay.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxDeadLetterStore` | `RequeueAsync(IReadOnlyList<Guid>)`, `RequeueAsync(Guid)` |
| `IOutboxDeadLetterStore` | `RequeueAsync(IReadOnlyList<Guid>)`, `RequeueAsync(Guid)` |

Both methods return `RequeueResult` with requested and actual requeue counts.

## SQL Behavior

- Requeue updates only rows currently in dead-letter status.
- Requeue reset includes:
  - status to pending
  - `visible_after` to null
  - `attempt_count` to zero
  - `lease_owner` and `lease_expires_at` to null
  - `last_error` to null

## Concurrency Model

- Requeue uses status predicate, so rows changed by another process are not incorrectly rewritten.
- Replay path is safe for single or batch id lists.

## Index Interaction

- Requeue targets primary key (`message_id`).
- No dedicated dead-letter index is required in current schema.

## Observability

- Automatic dead-letter transitions increment processor counters:
  - `litebus.inbox.processor.dead_lettered`
  - `litebus.outbox.processor.dead_lettered`
- Manual requeue changes queue-depth breakdown in diagnostics gauges on next scrape.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests for move-to-dead-letter and requeue semantics on inbox and outbox stores.
- Manager-facing behavior is validated indirectly through store contract coverage.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- End-to-end tests verify max-attempt transitions to dead letter for inbox and outbox.
- Hook-failure integration tests verify dead-letter transition after post-dispatch failures.
- PostgreSQL contract suites verify requeue behavior with concrete database state.

## Concrete Example

An operator requeues three dead-lettered message ids. The result reports `requested = 3`, `requeued = 2` when one id is no longer in dead-letter status.

## Related Docs

- [storage.role.state-writer](storage.role.state-writer.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
