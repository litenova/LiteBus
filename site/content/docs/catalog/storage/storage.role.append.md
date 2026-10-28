# Append Store Role

- **ID**: `storage.role.append`
- **Summary**: Writes inbox acceptance rows and outbox enqueue rows, with idempotency behavior.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxStore` | `AddAsync`, `AddBatchAsync` |
| `IOutboxStore` | `AddAsync`, `AddBatchAsync` |
| `ITransactionalInboxStore` | inherits `AddAsync`, `AddBatchAsync` bound to the caller transaction (marker interface, no new methods) |
| `ITransactionalOutboxStore` | inherits `AddAsync`, `AddBatchAsync` bound to the caller transaction (marker interface, no new methods) |

## SQL Behavior

- On PostgreSQL, append inserts with `ON CONFLICT DO NOTHING`.
- When a conflict occurs, the adapter returns the existing row with `AlreadyAccepted` or `AlreadyEnqueued` instead of creating a duplicate.
- A filtered unique index on `(tenant_id, idempotency_key)` enforces tenant-scoped idempotency.
- Batch append preserves request ordering in the returned append-result list. A later slot that duplicates an earlier slot reports the existing outcome.
- Strict identical replay compares tenants through normalized values, so a null request tenant matches a stored empty-string unscoped row and returns the existing outcome instead of throwing.
- Entity Framework batch recovery reinserts rows still missing after a unique violation and chains the database error as the inner exception when the retry also fails. Non-unique failures are rethrown instead of reported as existing rows.
- Outbox rows addressed to one handler by per-handler delivery store the handler full name in the `handler` column. Stores persist it as an opaque value and return it on lease and query.

## Concurrency Model

- Concurrent append calls are safe, because the primary key and idempotency unique indexes resolve duplicates.
- Batch append still applies deduplication behavior per envelope slot.
- Transactional mode can bind writes to the caller transaction through a manual or ambient participant.

## Index Interaction

- The `message_id` primary key prevents duplicate message identifiers.
- Inbox and outbox idempotency unique indexes enforce per-tenant deduplication.
- The outbox topic index relates to append, because `topic` is written at append time for publish routing.

## Observability

- The storage layer has no dedicated append counter.
- Appends appear in the queue-depth gauges on the next diagnostics pass:
  - `litebus.inbox.queue.depth`
  - `litebus.outbox.queue.depth`

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Inherited store contract tests validate duplicate message IDs, idempotency keys, ordered batch results, and later-slot duplicate outcomes for all adapters.
- EF transactional unit tests verify staged append behavior before `SaveChanges`.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStoreTests` and `PostgreSqlOutboxStoreTests` verify append contract parity.
- `PostgreSqlTransactionalWritersIntegrationTests` covers ambient and manual transactional append.
- `PostgreSqlReliableMessagingEndToEndTests` covers the append-to-process flow with a duplicate delivery scenario.

## Concrete Example

When a command with the same tenant and idempotency key is accepted twice, the second append call returns `InboxAppendResult(firstEnvelope, AlreadyAccepted)`. The processor then sees one pending message, not two.

## Related Docs

- [storage.transactional.writes](storage.transactional.writes.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
