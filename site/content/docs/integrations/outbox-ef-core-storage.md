# Outbox Entity Framework Core Storage

`LiteBus.Outbox.Storage.EntityFrameworkCore` provides `EfCoreOutboxStore`, which implements `IOutboxStore`, `IOutboxLeaseStore`, and `IOutboxStateWriter` on a single `DbContext`. Use it when the application already owns a `DbContext` and EF migrations.

For EF transactional enqueue (`ITransactionalOutbox<TContext>`), see [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md).

## Schema Ownership

LiteBus does not create or upgrade outbox tables for EF Core. Your application owns migrations. Apply `OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration()` in `OnModelCreating` so columns and indexes match `EfCoreOutboxStore`.

For Npgsql outbox storage and shared PostgreSQL schema helpers, see [PostgreSQL Schema Management](postgresql-schema-management.md).

## Registration

```csharp
services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);
        outbox.UseEntityFrameworkCoreStorage(options => options.UseDbContext<AppDbContext>());
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

`AppDbContext` must implement `IOutboxDbContext` and expose `DbSet<OutboxMessageEntity> OutboxMessages`. The adapter requires `IDbContextFactory<AppDbContext>` and owns one context per store operation.

## Transactional Writes

The default `IOutboxStore` registration creates a context through `IDbContextFactory<AppDbContext>` and calls `SaveChanges` during `EnqueueAsync`. That commits outbox rows in a separate unit of work from your application `DbContext` unless you opt in to one of the patterns below. See [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md) for the full pattern. This section covers the EF Core mechanics.

### Participate in the Caller `DbContext`

```csharp
var store = serviceProvider.GetRequiredService<EfCoreOutboxStore>();
var transactionalStore = store.UseExistingDbContext(appDbContext);

await transactionalStore.AddAsync(envelope, cancellationToken);
await appDbContext.SaveChangesAsync(cancellationToken);
```

`UseExistingDbContext` stages the outbox entity on the supplied context. The caller owns `SaveChanges` and any explicit transaction.

### `SaveChanges` Interceptor

Register the interceptor in LiteBus and on your `DbContext` options:

```csharp
builder.AddOutbox(outbox =>
{
    outbox.UseEntityFrameworkCoreStorage(storage =>
    {
        storage.UseDbContext<AppDbContext>();
        storage.EnableSaveChangesInterceptor();
    });
});

// In AddDbContext / OnConfiguring:
options.AddLiteBusOutboxInterceptor(
    serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
```

Queue envelopes before `SaveChanges`:

```csharp
interceptor.Enqueue(appDbContext, envelope);
await appDbContext.SaveChangesAsync(cancellationToken);
```

`LiteBusOutboxSaveChangesInterceptor` copies queued envelopes into `IOutboxDbContext.OutboxMessages` during `SavingChanges`, so domain entities and outbox rows share one EF transaction. The flushed rows carry the full staged envelope, including the per-handler delivery target, so targeted envelopes keep their addressing through the interceptor path.

When `EnableSaveChangesInterceptor()` is set, the module also registers the non-generic `ITransactionalOutbox` over `ITransactionalOutbox<TContext>`, so `StageCollectedEvents()` drains collected domain events into the ambient EF transaction.

## Default Table and Columns

| Setting | Default |
| --- | --- |
| Schema | `public` |
| Table | `litebus_outbox_messages` |

`GetModelBuilderConfiguration()` maps:

| Column | Role |
| --- | --- |
| `message_id` | Primary key (`Guid`, application-assigned) |
| `contract_name`, `contract_version` | Stable message contract |
| `payload` | JSON serialized event (store type depends on provider; see below) |
| `topic` | Optional routing hint for external dispatchers |
| `created_at`, `visible_after`, `attempt_count`, `status` | Scheduling and lifecycle |
| `lease_owner`, `lease_expires_at` | Processor leasing |
| `lease_generation` | Fencing counter incremented on each lease |
| `last_error`, `error_type` | Last failure message and the exception type full name (or type name when the full name is unavailable) |
| `published_at`, `last_attempted_at`, `first_failed_at`, `dead_lettered_at` | Forensic timestamps for terminal state, last attempt, first failure, and dead-lettering |
| `last_lease_owner` | Owner of the most recent expired or completed lease |
| `correlation_id`, `causation_id`, `tenant_id` | Optional metadata |
| `idempotency_key` | Optional deduplication scoped per `tenant_id` (unique composite index with `IS NOT NULL` filter) |
| `trace_context` | Optional distributed trace payload (`jsonb` on PostgreSQL) |

Operators query `last_error`, `error_type`, `last_lease_owner`, and the forensic timestamps. Processors use `status`, `visible_after`, `lease_expires_at`, and `lease_generation`.

The indexes are a unique filtered index on `(tenant_id, idempotency_key)`, a composite index on `(status, visible_after, lease_expires_at, created_at)`, a chronological index `IX_LiteBus_Outbox_CreatedAt`, and a filtered index on `topic` where `topic IS NOT NULL`. Null or whitespace `tenant_id` values normalize to an empty string for idempotency scope.

Override names with `EntityFrameworkCoreOutboxStoreOptions` when needed.

## Provider-Specific Model Configuration

Pass `EfCoreStorageProvider` to `GetModelBuilderConfiguration()` so payload and trace columns use the correct store types. LiteBus maps `payload` as `TEXT` for every EF provider because protected payloads are opaque strings. PostgreSQL maps `trace_context` as nullable `jsonb` when `EfCoreStorageProvider.PostgreSql` is supplied. Omitting the provider leaves both properties for application-owned column configuration.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var outboxOptions = new EntityFrameworkCoreOutboxStoreOptions();

    if (Database.IsNpgsql())
    {
        modelBuilder.GetModelBuilderConfiguration(outboxOptions, EfCoreStorageProvider.PostgreSql);
        return;
    }

    modelBuilder.GetModelBuilderConfiguration(outboxOptions);
}
```

See [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md) for the full provider column-type table. SQL Server applications should set `SchemaName = "dbo"` in store options so raw lease SQL matches your migrations.

Supported leasing providers match inbox EF storage: PostgreSQL, SQL Server, MySQL (Pomelo), SQLite, and the in-memory provider for tests. MySQL uses `READ COMMITTED`, `FOR UPDATE SKIP LOCKED`, and `IX_LiteBus_Outbox_CreatedAt` for ordered concurrent claims. SQLite stores durable timestamps as UTC ticks and serializes lease writers through its database transaction. The inbox storage page lists the full provider table and the SQLite throughput constraint.

## Align Store Options with Your EF Model

Pass the same `EntityFrameworkCoreOutboxStoreOptions` schema and table names to `GetModelBuilderConfiguration()` in `OnModelCreating` as you register through `UseEntityFrameworkCoreStorage`. See [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md) for the same pitfall on inbox storage.

## Processing Notes

`EfCoreOutboxStore` reloads rows after insert when the store normalizes JSON payloads. Leasing uses the same multi-provider skip-locked SQL as `EfCoreInboxStore`.

Register storage and a dispatcher before `EnableOutboxProcessor()`. Module build fails when the processor is enabled without a dispatcher.

## Idempotency on the EF Transactional Path

`EfCoreOutboxStore` and `ITransactionalOutbox<TContext>` honor `Idempotency.Keyed` on enqueue metadata:

| `IdempotencyConflictMode` | Behavior on duplicate key in one transaction |
| --- | --- |
| `ReturnExisting` (default) | Returns the existing receipt without inserting a second row. |
| `Strict` | Throws `IdempotencyConflictException` before `SaveChanges`, but only when the conflicting row differs. A byte-identical replay returns the existing receipt. |

`Strict` compares message id, contract name, contract version, payload, topic, handler, idempotency key, correlation id, causation id, and tenant id. Rows that match on all ten fields return the stored receipt. Any difference throws.

The `LiteBusOutboxSaveChangesInterceptor` path does not deduplicate at enqueue time. A duplicate `(tenant_id, idempotency_key)` pair in the same `SaveChanges` batch surfaces as a `DbUpdateException` from the unique index and aborts the caller transaction. Use `ReturnExisting` on `IOutbox` / `ITransactionalOutbox` when you need silent deduplication. Reserve the interceptor for envelopes staged without idempotency keys or when duplicate keys should fail the unit of work.

## Alignment with PostgreSQL `CurrentSchemaVersion`

`PostgreSqlOutboxSchema.CurrentSchemaVersion` is **1**. The EF model includes `lease_generation` for fencing, opaque payload text, and nullable `trace_context`. Pass `EfCoreStorageProvider.PostgreSql` to `GetModelBuilderConfiguration()` so the fluent model maps `trace_context` as optional `jsonb`. Generate an application-owned migration from the complete current model.

Generate the complete current model into a new table when an existing table has an incompatible shape. Follow the row-mapping checks in the [Migration Guide](../migration/v6.md) when historical data must be retained.

## Related Docs

- [Outbox](../reliable-messaging/outbox.md)
- [Hosted Services](../architecture/hosted-services.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
- [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md)
