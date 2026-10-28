# Dead-Letter Store Role

- **ID**: `storage.role.dead-letter`
- **Summary**: Requeues dead-lettered rows so operators can replay them.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxDeadLetterStore` | `RequeueAsync(IReadOnlyList<Guid>)`, `RequeueAsync(Guid)` |
| `IOutboxDeadLetterStore` | `RequeueAsync(IReadOnlyList<Guid>)`, `RequeueAsync(Guid)` |

Both methods return `RequeueResult` with requested and actual requeue counts.

## SQL Behavior

- Requeue updates only rows that are currently in dead-letter status.
- A requeue resets:
  - status to pending
  - `visible_after` to null
  - `attempt_count` to zero
  - `lease_owner` and `lease_expires_at` to null
  - `last_error` and `error_type` to null
  - `first_failed_at` and `dead_lettered_at` to null

## Concurrency Model

- Requeue uses a status predicate, so a row that another process moved out of dead-letter status keeps its new state.
- The replay path is safe for single and batch id lists.

## Index Interaction

- Requeue targets the primary key (`message_id`).
- The current schema requires no dedicated dead-letter index.

## Observability

- Automatic dead-letter transitions increment processor counters:
  - `litebus.inbox.processor.dead_lettered`
  - `litebus.outbox.processor.dead_lettered`
- Manual requeue changes the queue-depth breakdown in the diagnostics gauges on the next scrape.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Contract tests cover move-to-dead-letter and requeue semantics on inbox and outbox stores.
- Store contract coverage indirectly validates the manager-facing behavior.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- End-to-end tests verify max-attempt transitions to dead letter for inbox and outbox.
- Hook-failure integration tests verify dead-letter transition after post-dispatch failures.
- PostgreSQL contract suites verify requeue behavior with concrete database state.

## Concrete Example

An operator requeues three dead-lettered message ids. The result reports `requested = 3`, `requeued = 2` when one id has left dead-letter status.

## Related Docs

- [storage.role.state-writer](storage.role.state-writer.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
