# Outbox Marten Adapter

- **ID**: `storage.outbox.adapter.marten`
- **Package**: `LiteBus.Outbox.Storage.Marten` (feature bridge)
- **Shared dependency**: `LiteBus.Outbox.Storage.PostgreSql` for the schema-owned insert. Marten is referenced directly, and the application registers it with `AddMarten`
- **Summary**: A scoped `MartenTransactionalOutbox` queues outbox rows on the current Marten `IDocumentSession`, so documents and outbox rows commit in one `SaveChangesAsync`.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `ITransactionalOutbox` | `EnqueueAsync`, `EnqueueBatchAsync` |

## Marten Behavior

- Register with `outbox.UsePostgreSqlStorage(postgres => postgres.UseMartenTransactionalOutbox())`. LiteBus does not register Marten. The application calls `AddMarten` against the same database as the outbox.
- Each staged envelope is queued on the scoped session with `QueueSqlCommand('?', statement.FormatSql('?'), [.. statement.Values])` using the schema-owned insert from `PostgreSqlOutboxSchema.CreateInsertStatement`. The row therefore matches a PostgreSQL store row in every column.
- The row is written only when the session `SaveChangesAsync` commits. Disposing the session without saving stores nothing.
- The writer resolves idempotency before queueing. It first checks envelopes already staged by the same instance, then reads committed rows through a lookup store on a separate connection, and queues only when both checks find nothing. `ReturnExisting` returns the stored receipt without queueing a second row. `Strict` returns the stored receipt for an identical replay and throws `IdempotencyConflictException` when the conflicting submission differs.
- Staging the same message twice in one session queues one row and returns `AlreadyEnqueued` for the second attempt.
- When a concurrent insert of the same id or key commits first, the queued insert is skipped by `ON CONFLICT DO NOTHING`, so the receipt reports `Enqueued` even though the database kept the other row.
- `UseMartenTransactionalOutbox()` cannot be combined with `EnableAmbientTransactionProvider()`. Both register `ITransactionalOutbox`, so module build throws `OutboxPostgreSqlStorageConfigurationException`.

## Concurrency Model

- The session serializes staged inserts, and the `message_id` primary key and the tenant-scoped idempotency unique index reject duplicates that another writer commits between the lookup and the queue.
- Processors publish from the singleton PostgreSQL store on their own pooled connections. Only the enqueue path goes through the session.
- Use this writer when one Marten session serves reads and writes and opening an application-owned write transaction for every read is not acceptable. When the application owns the transaction and Marten joins it, use the ambient provider instead.

## Observability

- There is no diagnostic probe. The outbox table is owned by the PostgreSQL schema (`outbox.postgresql.schema`), and Marten owns its document schema.
- Committed rows appear in `litebus.outbox.queue.depth` on the next diagnostics pass like any other enqueue.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `MartenQueuedTransactionalOutboxTests` covers committing a document and an outbox row together, storing nothing without save, storing one row for a twice-staged message, `AlreadyEnqueued` for a stored id, throwing on a strict conflict, and throwing at composition when combined with the ambient provider.

## Related Docs

- [Outbox Marten Storage](../../integrations/outbox-marten-storage.md)
- [storage.outbox.adapter.postgresql](storage.outbox.adapter.postgresql.md)
- [storage.transactional.writes](storage.transactional.writes.md)
- [Outbox](../../reliable-messaging/outbox.md)
