# Outbox Marten Storage

`LiteBus.Outbox.Storage.Marten` provides `MartenTransactionalOutbox`, an `ITransactionalOutbox` that queues the schema-owned outbox insert on the current Marten `IDocumentSession` with `QueueSqlCommand`. Use it when one Marten session serves reads and writes and opening an application-owned write transaction for every read is not acceptable.

For the ambient-transaction route, see [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md).

## Install

Reference `LiteBus.Outbox`, `LiteBus.Outbox.Storage.PostgreSql`, and `LiteBus.Outbox.Storage.Marten`. Your application already references Marten for `AddMarten`; the storage package targets the same database through the shared PostgreSQL schema helpers.

## Register

```csharp
services.AddMarten(options =>
{
    options.Connection(connectionString);
});

services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);
        outbox.UsePostgreSqlStorage(pg =>
        {
            pg.UseDataSource(dataSource);
            pg.UseMartenTransactionalOutbox();
        });
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

LiteBus does not register Marten: the application calls `AddMarten` against the same database as the outbox, and the writer resolves the scoped `IDocumentSession` from the current scope. `UseMartenTransactionalOutbox()` cannot be combined with `EnableAmbientTransactionProvider()`: both register `ITransactionalOutbox`, so module build throws `OutboxPostgreSqlStorageConfigurationException` when both are configured.

## Enqueue in a Marten Session

```csharp
var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
session.Store(order);
await transactionalOutbox.EnqueueAsync(
    new OrderSubmitted { OrderId = order.Id },
    cancellationToken).ConfigureAwait(false);
await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

The handler and the writer resolve the same scoped session, so the document and the outbox row commit in one `SaveChangesAsync`. Disposing the session without saving stores neither. Processors publish from the singleton PostgreSQL store on their own connections; only the enqueue path goes through the session.

## Idempotency

The writer resolves duplicates before queueing because the queued insert ends with `ON CONFLICT DO NOTHING` and reports nothing about skipped rows. `ReturnExisting` (the default) returns the stored receipt without queueing a second row. `Strict` returns the stored receipt for a byte-identical replay and throws `IdempotencyConflictException` when the conflicting submission differs. Staging the same message twice in one session queues one row and returns `AlreadyEnqueued` for the second attempt.

## Choosing Between the Queued Writer and the Ambient Transaction

| Your setup | Use |
| --- | --- |
| One Marten session serves reads and writes; the application owns no transaction | This package: `UseMartenTransactionalOutbox()` queues the insert on the session |
| The application owns the transaction (middleware or unit of work) and Marten joins it | [Ambient provider](../reliable-messaging/transactional-writes.md#postgresql-ambient-provider): `EnableAmbientTransactionProvider()` plus `SessionOptions.ForTransaction` |

Both paths write the same 18 columns through the schema-owned insert. The difference is the commit boundary: the session save or the application transaction commit.

## Limitations

Staged rows are invisible until `SaveChangesAsync` commits. A concurrent insert of the same id or key that wins the race is absorbed by `ON CONFLICT DO NOTHING`, so the receipt then says `Enqueued` though the other row was kept. Every enqueue performs one extra read for the idempotency lookup on a separate connection. Marten must target the same database as the outbox. Marten assemblies are not strong-named.

## Related Docs

- [Outbox](../reliable-messaging/outbox.md)
- [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
- [Outbox Entity Framework Core Storage](outbox-ef-core-storage.md)
