# Append Store Role

- **ID**: `storage.role.append`
- **Summary**: Writes inbox acceptance rows and outbox enqueue rows, including idempotency behavior.

## Interface Methods

| Role | Methods |
| --- | --- |
| `IInboxStore` | `AddAsync`, `AddBatchAsync` |
| `IOutboxStore` | `AddAsync`, `AddBatchAsync` |
| `ITransactionalInboxStore` | transactional variants of append methods |
| `ITransactionalOutboxStore` | transactional variants of append methods |

## SQL Behavior

- PostgreSQL append uses insert with `ON CONFLICT DO NOTHING`.
- When conflict occurs, adapter returns existing row instead of creating a duplicate.
- Tenant-scoped idempotency is enforced by filtered unique index on `(tenant_id, idempotency_key)`.
- Batch append preserves request ordering in returned envelope list.

## Concurrency Model

- Concurrent append calls are safe because primary key and idempotency unique indexes resolve duplicates.
- Batch append still applies dedup behavior per envelope slot.
- Transactional mode can bind writes to caller transaction (manual or ambient participant).

## Index Interaction

- `message_id` primary key prevents duplicate message identifiers.
- Inbox and outbox idempotency unique indexes enforce per-tenant dedup.
- Outbox topic index is append-adjacent because `topic` is written at append time for publish routing.

## Observability

- No dedicated append counter at storage layer.
- Append impact appears in queue-depth gauges on next diagnostics pass:
  - `litebus.inbox.queue.depth`
  - `litebus.outbox.queue.depth`

## Test Coverage

### `LiteBus.Storage.UnitTests`

- Inherited store contract tests validate duplicate message id and idempotency semantics for all adapters.
- EF transactional unit tests verify staged append behavior before `SaveChanges`.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStoreTests` and `PostgreSqlOutboxStoreTests`: append contract parity.
- `PostgreSqlTransactionalWritersIntegrationTests`: ambient and manual transactional append.
- `PostgreSqlReliableMessagingEndToEndTests`: append-to-process flow with duplicate delivery scenario.

## Concrete Example

When a command with the same tenant and idempotency key is accepted twice, the second append call returns the first stored row. Processor logic then sees one pending message, not two.

## Related Docs

- [storage.transactional.writes](storage.transactional.writes.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
