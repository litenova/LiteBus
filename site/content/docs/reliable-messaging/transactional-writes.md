# Transactional Messaging Writes

A command handler that saves an aggregate and enqueues an integration event needs both writes in one database transaction, so they commit or roll back together. This page selects the writer API for each stack and shows the registration for EF Core, PostgreSQL with Marten, and raw ADO.NET. Read [Reliable messaging](README.md) first if you are new to inbox and outbox.

This page owns the transactional writer pattern: which API to inject, how LiteBus participates in a caller-owned transaction, and the registration for each stack. The EF Core storage guides defer here for writer choice and interceptor behavior.

## What This Page Covers

| Topic | Covered here |
| --- | --- |
| `IInbox` / `IOutbox` vs `ITransactionalInbox` / `ITransactionalOutbox` | Yes |
| EF Core with shared `DbContext` | Yes |
| PostgreSQL with ambient `IPostgreSqlTransactionProvider` | Yes |
| Manual PostgreSQL bind (`CreateTransactionalStore`) | Yes |
| Ingress, processors, connection pooling | Yes (brief) |
| Custom database adapters | Pointer only: see [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md) |

## What This Does Not Cover

- Broker ingress wiring (AMQP, Kafka, and similar): see [Inbox](inbox.md) and transport guides.
- Processor lease, retry, and dead-letter tuning: see [Inbox](inbox.md) and [Outbox](outbox.md).
- Marten or EF configuration beyond transaction enlistment: see those libraries' docs.

## The Problem

A command handler saves an aggregate and enqueues an integration event. If those writes use different transactions, two failure modes follow:

- Outbox row committed, domain change rolled back (event published for state that never existed).
- Domain change committed, outbox missing (state saved, notification lost).

LiteBus solves this by letting the writer store participate in a transaction you already opened. LiteBus does not open or commit your domain transaction.

## Two Writer APIs

LiteBus keeps both APIs. They are not interchangeable inside a command handler that already holds an open transaction.

| API | Use when | What happens on `AcceptAsync` / `EnqueueAsync` |
| --- | --- | --- |
| `IInbox` / `IOutbox` | Ingress, operator tools, tests without shared domain work | Singleton store opens its own connection and commits immediately |
| `ITransactionalInbox` / `ITransactionalOutbox` | Command handler plus domain persistence in the same PostgreSQL unit of work | Bound store inserts inside your transaction; you call `SaveChangesAsync` or `CommitAsync` |
| `ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>` | EF Core apps with a shared `DbContext` | Envelopes staged on your context until `SaveChangesAsync` |

Background processors always use `IInbox` / `IOutbox` against the singleton store. They use their own pooled connections, not your HTTP request connection.

```mermaid
flowchart LR
  subgraph request [Your command scope]
    Domain[Domain persistence]
    TxWriter["ITransactionalOutbox"]
    Domain --> SameTx[One transaction]
    TxWriter --> SameTx
  end
  subgraph background [Processor loop]
    Proc[PipelinedOutboxProcessor]
    Proc --> OwnConn[Separate pooled connection]
  end
```

Batch writers (`AcceptBatchAsync`, `EnqueueBatchAsync`) exist on every writer API. On the immediate APIs a batch commits in one store round trip; on the transactional APIs the batch stages inside the caller transaction and commits atomically with it, with receipts in input order on both paths.

## Connection Sharing

Register one `NpgsqlDataSource` per database and pass it to inbox, outbox, and your application (Marten, Dapper, or similar):

```csharp
var dataSource = NpgsqlDataSource.Create(configuration.GetConnectionString("Orders")!);
services.AddSingleton(dataSource);
```

Call `UseDataSource(dataSource)` on both `UsePostgreSqlStorage` builders. Calling `UseConnectionString` separately on inbox and outbox creates duplicate data sources against the same database (redundant, harder to reason about).

Per request, use one connection and one transaction for atomic domain plus messaging writes. Multiple connections from the same pool across the process (request plus inbox processor plus outbox processor) is normal and correct.

## How Participation Works

1. You open the transaction (or own the `DbContext` that will call `SaveChanges`).
2. You bind LiteBus to that boundary: EF interceptor, `IPostgreSqlTransactionProvider`, or `CreateTransactionalStore`.
3. Handler calls `ITransactionalOutbox.EnqueueAsync` (or inbox accept).
4. LiteBus serializes the message and runs `INSERT` on your connection (or stages on your `DbContext`).
5. You commit once. LiteBus does not commit your domain work.

## Choose a Path

| Your stack | Start here |
| --- | --- |
| EF Core `DbContext` for domain plus outbox table | [EF Core](#ef-core-shared-dbcontext) |
| PostgreSQL plus Marten, Dapper, or raw SQL | [Ambient provider](#postgresql-ambient-provider) (recommended) or [Manual bind](#postgresql-manual-bind) |
| AMQP/Kafka ingress only storing commands | [Ingress](#ingress-auto-commit): use `IInbox`, not transactional writers |
| Unit tests with `UseInMemoryStorage` | [Tests](#tests-and-in-memory) |

---

## EF Core (Shared DbContext)

**Packages:** `LiteBus.Outbox.Storage.EntityFrameworkCore` and/or `LiteBus.Inbox.Storage.EntityFrameworkCore`

**Writer API:** `ITransactionalOutbox<TContext>` / `ITransactionalInbox<TContext>`

**Registration:**

```csharp
services.AddDbContext<OrdersDbContext>((serviceProvider, options) =>
    options.UseNpgsql(connectionString)
        .AddLiteBusOutboxInterceptor(
            serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

services.AddDbContextFactory<OrdersDbContext>((serviceProvider, options) =>
    options.UseNpgsql(connectionString)
        .AddLiteBusOutboxInterceptor(
            serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

builder.AddOutbox(outbox =>
{
    outbox.UseEntityFrameworkCoreStorage(ef =>
    {
        ef.UseDbContext<OrdersDbContext>();
        ef.EnableSaveChangesInterceptor();
        ef.EnforceTransactionalSetup();
    });
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

`OrdersDbContext` must implement `IOutboxDbContext` and expose `DbSet<OutboxMessageEntity>`. Full schema steps: [Outbox Entity Framework Core storage](../integrations/outbox-ef-core-storage.md).

Register both the factory and the scoped context. The factory (`IDbContextFactory<OrdersDbContext>`) supplies operation contexts to the singleton LiteBus store, while `ITransactionalOutbox<OrdersDbContext>` resolves the scoped `OrdersDbContext` from the current scope; factory-only registration cannot satisfy the transactional path. LiteBus does not create an `IServiceScope` inside the EF store.

`EnforceTransactionalSetup()` is a fail-fast guard: module build throws when `EnableSaveChangesInterceptor()` was not called, so a missing interceptor registration fails at startup instead of at the first save.

**Handler:**

```csharp
await domainRepository.SaveOrderAsync(order, cancellationToken).ConfigureAwait(false);
await transactionalOutbox.EnqueueAsync(
    new OrderSubmittedIntegrationEvent { OrderId = order.Id },
    cancellationToken).ConfigureAwait(false);
await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

**Commit owner:** `SaveChangesAsync` (and optional explicit `BeginTransactionAsync` on the context).

Duplicate `message_id` or `idempotency_key` on the transactional EF path fails `SaveChanges` and rolls back the whole unit of work.

---

## PostgreSQL (Ambient Provider)

**Packages:** `LiteBus.Outbox.Storage.PostgreSql`, `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Storage.PostgreSql` (for `IPostgreSqlTransactionProvider`)

**Writer API:** `ITransactionalOutbox` / `ITransactionalInbox` (non-generic)

Use this path when domain persistence is Marten, Dapper, or ADO.NET and you want handlers to inject transactional writers without manual store binding.

### 1. LiteBus Registration

```csharp
services.AddSingleton(dataSource);

builder.AddOutbox(outbox =>
{
    outbox.UsePostgreSqlStorage(pg =>
    {
        pg.UseDataSource(dataSource);
        pg.EnableAmbientTransactionProvider(); // default: RequireActiveTransaction
    });
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

Register the same `dataSource` on inbox if you use both axes.

### 2. Application Transaction Provider

Implement scoped `IPostgreSqlTransactionProvider`. Activate it when your unit of work starts (middleware, command pre-handler, or scoped service):

```csharp
public sealed class OrderUnitOfWork : IPostgreSqlTransactionProvider, IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private readonly NpgsqlDataSource _dataSource;

    public OrderUnitOfWork(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        _connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryGetCurrent(
        [NotNullWhen(true)] out NpgsqlConnection? connection,
        [NotNullWhen(true)] out NpgsqlTransaction? transaction)
    {
        connection = _connection;
        transaction = _transaction;
        return _connection is not null && _transaction is not null;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

Register both the unit of work and the provider alias:

```csharp
services.AddScoped<OrderUnitOfWork>();
services.AddScoped<IPostgreSqlTransactionProvider>(sp => sp.GetRequiredService<OrderUnitOfWork>());
```

### 3. Enlist Marten on the Same Transaction

Your unit of work implements `IPostgreSqlTransactionProvider` and owns the transaction. Marten joins it through `SessionOptions.ForTransaction`, and LiteBus resolves a bound store from the same provider when the handler injects `ITransactionalOutbox`:

```csharp
await unitOfWork.BeginAsync(cancellationToken).ConfigureAwait(false);

if (!unitOfWork.TryGetCurrent(out _, out var transaction))
{
    throw new InvalidOperationException("Unit of work did not activate a transaction.");
}

await using var session = documentStore.LightweightSession(SessionOptions.ForTransaction(transaction));

session.Store(order);
await transactionalOutbox.EnqueueAsync(
    new OrderSubmittedIntegrationEvent { OrderId = order.Id },
    cancellationToken).ConfigureAwait(false);
await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
```

Queries use a separately resolved session and never open a write transaction. The session above is constructed at the point of the write. Registering one Marten session for both reads and writes is what would make every query hold a write transaction for its duration; that is an application composition choice, not something the ambient provider requires.

`ITransactionalOutbox` binds its store when it is resolved, not when `EnqueueAsync` is called. A handler that injects it without an open unit of work therefore fails at injection with `InvalidOperationException`, which is what `TransactionalWriteMode.RequireActiveTransaction` is for: the alternative would be silently committing the outbox row on its own connection.

### `TransactionalWriteMode`

| Mode | Behavior |
| --- | --- |
| `RequireActiveTransaction` (default) | Throws if `TryGetCurrent` is false: use in production handlers |
| `AllowImmediateCommit` | Falls back to singleton auto-commit store when no transaction is active: dev/tests only; can break atomicity silently |

---

## PostgreSQL (Manual Bind)

**Writer API:** construct `StoreBoundTransactionalOutbox` (or inject `ITransactionalOutbox` only if you wrap this in your own scoped factory)

Use when you prefer explicit control and do not enable `EnableAmbientTransactionProvider()`.

```csharp
var registration = serviceProvider.GetRequiredService<PostgreSqlOutboxStoreRegistration>();

await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

var store = registration.CreateTransactionalStore(connection, transaction);
var writer = new StoreBoundTransactionalOutbox(
    store,
    serviceProvider.GetRequiredService<IOutboxEnvelopeFactory>());

// Domain SQL or enlisted ORM on the same connection/transaction
await writer.EnqueueAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
```

`IInboxEnvelopeFactory` / `IOutboxEnvelopeFactory` handle contract resolution and serialization so you do not build `InboxEnvelope` / `OutboxEnvelope` by hand. The inbox manual path mirrors this shape with `StoreBoundTransactionalInbox`, which takes the same two constructor arguments.

---

## Ingress (Auto-Commit)

Broker ingress calls `IInbox.AcceptAsync`. That path auto-commits to the singleton store. There is no shared domain transaction at the edge.

If a later command handler must accept into inbox and mutate domain state atomically (uncommon), use `ITransactionalInbox` with the same PostgreSQL patterns above inside that handler: not at ingress.

---

## Tests and In-Memory

`UseInMemoryStorage` has no database transaction. Use `IInbox` / `IOutbox` for immediate commits. Cross-store atomicity with a real database is not available on the in-memory path.

PostgreSQL transactional tests live under `tests/LiteBus.Storage.IntegrationTests` and `tests/LiteBus.Durable.IntegrationTests`. Run with Docker or `LITEBUS_TEST_POSTGRES_CONNECTION_STRING`. See [Integration tests](../testing/integration-tests.md).

---

## Anti-Patterns

| Mistake | Why it fails |
| --- | --- |
| Inject `IOutbox` inside a handler that already opened a domain transaction | Outbox commits in a separate transaction |
| `UseConnectionString` on inbox and outbox separately when both use one database | Duplicate `NpgsqlDataSource` instances |
| Expect the processor to reuse the request connection | Processors lease on their own connections by design |
| `EnableAmbientTransactionProvider()` without registering `IPostgreSqlTransactionProvider` | `RequireActiveTransaction` throws at enqueue time |

---

## Troubleshooting

**Outbox row exists but domain change rolled back**

- Handler used `IOutbox` instead of `ITransactionalOutbox`.
- Ambient provider was not active (`TryGetCurrent` returned false).
- Manual bind used a different connection than domain SQL.

**`requires an active PostgreSQL transaction`**

- `EnableAmbientTransactionProvider()` with default mode, but no provider activation before enqueue. Open and expose the transaction at UoW start. See [Troubleshooting](../operations/troubleshooting.md).

---

## Next

- Registration reference: [Outbox](outbox.md), [Inbox](inbox.md)
- Domain events to integration events: [Domain events and unit of work](domain-events-and-unit-of-work.md)
- Custom databases: [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
- Cookbook recipes: [Cookbook and scenarios](../getting-started/cookbook.md): transactional outbox with PostgreSQL
