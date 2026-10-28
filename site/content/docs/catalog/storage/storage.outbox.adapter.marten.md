# Outbox Marten Adapter

- **ID**: `storage.outbox.adapter.marten`
- **Package**: `LiteBus.Outbox.Storage.Marten` (feature bridge)
- **Shared dependency**: `LiteBus.Outbox.Storage.PostgreSql` for the schema-owned insert; Marten is referenced directly and the application registers it with `AddMarten`
- **Summary**: A scoped `MartenTransactionalOutbox` queues outbox rows on the current Marten `IDocumentSession`, so documents and outbox rows commit in one `SaveChangesAsync`.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `ITransactionalOutbox` | `EnqueueAsync`, `EnqueueBatchAsync` |

## Marten Behavior

- Registration: `outbox.UsePostgreSqlStorage(postgres => postgres.UseMartenTransactionalOutbox())`; LiteBus does not register Marten, and the application calls `AddMarten` against the same database as the outbox.
- Each staged envelope is queued on the scoped session with `QueueSqlCommand('?', statement.FormatSql('?'), [.. statement.Values])` using the schema-owned insert from `PostgreSqlOutboxSchema.CreateInsertStatement`, so the row matches the PostgreSQL store row column for column.
- The row is written only when the session `SaveChangesAsync` commits; disposing the session without saving stores nothing.
- Idempotency is resolved before queueing: the writer first checks envelopes already staged by the same instance, then reads committed rows through a lookup store on a separate connection, and only then queues. `ReturnExisting` returns the stored receipt without queueing a second row; `Strict` returns the stored receipt for an identical replay and throws `IdempotencyConflictException` when the conflicting submission differs.
- Staging the same message twice in one session queues one row and returns `AlreadyEnqueued` for the second attempt.
- A concurrent insert of the same id or key that wins the race is absorbed by `ON CONFLICT DO NOTHING`, so the receipt then says `Enqueued` though the other row was kept.
- `UseMartenTransactionalOutbox()` cannot be combined with `EnableAmbientTransactionProvider()`: both register `ITransactionalOutbox`, so module build throws `OutboxPostgreSqlStorageConfigurationException`.

## Concurrency Model

- The session serializes staged inserts; the lookup-then-queue window is closed by the `message_id` primary key and the tenant-scoped idempotency unique index.
- Processors publish from the singleton PostgreSQL store on their own pooled connections; only the enqueue path goes through the session.
- Use this writer when one Marten session serves reads and writes and opening an application-owned write transaction for every read is not acceptable; when the application owns the transaction and Marten joins it, use the ambient provider instead.

## Observability

- No diagnostic probe: the outbox table is owned by the PostgreSQL schema (`outbox.postgresql.schema`), and Marten owns its document schema.
- Committed rows appear in `litebus.outbox.queue.depth` on the next diagnostics pass like any other enqueue.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `MartenQueuedTransactionalOutboxTests`: document and outbox row committing together, nothing stored without save, one row for a twice-staged message, `AlreadyEnqueued` for a stored id, strict conflict throwing, and the ambient-provider combination throwing at composition.

## Related Docs

- [Outbox Marten Storage](../../integrations/outbox-marten-storage.md)
- [storage.outbox.adapter.postgresql](storage.outbox.adapter.postgresql.md)
- [storage.transactional.writes](storage.transactional.writes.md)
- [Outbox](../../reliable-messaging/outbox.md)
