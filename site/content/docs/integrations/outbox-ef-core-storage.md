# Outbox Entity Framework Core Storage

`LiteBus.Outbox.Storage.EntityFrameworkCore` provides `EfCoreOutboxStore`, which implements `IOutboxStore`, `IOutboxLeaseStore`, and `IOutboxStateWriter` on a single `DbContext`.

For EF transactional enqueue (`ITransactionalOutbox<TContext>`), see [Transactional messaging writes](../reliable-messaging/transactional-writes.md).

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

The default `IOutboxStore` registration creates a context through `IDbContextFactory<AppDbContext>` and calls `SaveChanges` during `EnqueueAsync`. That commits outbox rows in a separate unit of work from your application `DbContext` unless you opt in to one of the patterns below.

### Participate in the Caller `DbContext`

```csharp
var store = serviceProvider.GetRequiredService<EfCoreOutboxStore>();
var transactionalStore = store.UseExistingDbContext(appDbContext);

await transactionalStore.EnqueueAsync(envelope, cancellationToken);
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
interceptor.Enqueue(envelope);
await appDbContext.SaveChangesAsync(cancellationToken);
```

`LiteBusOutboxSaveChangesInterceptor` copies queued envelopes into `IOutboxDbContext.OutboxMessages` during `SavingChanges`, so domain entities and outbox rows share one EF transaction.

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
| `last_error` | Last failure message |
| `correlation_id`, `causation_id`, `tenant_id` | Optional metadata |
| `idempotency_key` | Optional deduplication scoped per `tenant_id` (unique composite index with `IS NOT NULL` filter) |

Indexes: unique filtered index on `(tenant_id, idempotency_key)`; composite index on `(status, visible_after, lease_expires_at, created_at)`; chronological index `IX_LiteBus_Outbox_CreatedAt`; filtered index on `topic` where `topic IS NOT NULL`. Null or whitespace `tenant_id` values normalize to an empty string for idempotency scope.

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

See [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md) for the full provider column-type table. SQL Server apps should set `SchemaName = "dbo"` in store options so raw lease SQL matches your migrations.

Supported leasing providers match inbox EF storage: PostgreSQL, SQL Server, MySQL (Pomelo), SQLite, and the in-memory provider for tests. MySQL uses `READ COMMITTED`, `FOR UPDATE SKIP LOCKED`, and `IX_LiteBus_Outbox_CreatedAt` for ordered concurrent claims. SQLite stores durable timestamps as UTC ticks and serializes lease writers through its database transaction. See the inbox guide for the full provider table and SQLite throughput constraint.

## Align Store Options with Your EF Model

Pass the same `EntityFrameworkCoreOutboxStoreOptions` schema and table names to `GetModelBuilderConfiguration()` in `OnModelCreating` as you register through `UseEntityFrameworkCoreStorage`. See [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md) for the same pitfall on inbox storage.

## Processing Notes

`EfCoreOutboxStore` reloads rows after insert when the store normalizes JSON payloads. Leasing uses the same multi-provider skip-locked SQL as `EfCoreInboxStore`.

Register dispatchers before `EnableOutboxProcessor()`.

## Idempotency on the EF Transactional Path

`EfCoreOutboxStore` and `ITransactionalOutbox<TContext>` honor `Idempotency.Keyed` on enqueue metadata:

| `IdempotencyConflictMode` | Behavior on duplicate key in one transaction |
| --- | --- |
| `ReturnExisting` (default) | Returns the existing receipt without inserting a second row. |
| `Strict` | Throws `IdempotencyConflictException` before `SaveChanges`. |

The `LiteBusOutboxSaveChangesInterceptor` path does not dedupe at enqueue time. A duplicate `(tenant_id, idempotency_key)` pair in the same `SaveChanges` batch surfaces as a `DbUpdateException` from the unique index and aborts the caller transaction. Use `ReturnExisting` on `IOutbox` / `ITransactionalOutbox` when you need silent dedupe; reserve the interceptor for envelopes staged without idempotency keys or when duplicate keys should fail the unit of work.

## Alignment with PostgreSQL `CurrentSchemaVersion`

`PostgreSqlOutboxSchema.CurrentSchemaVersion` is **3**. The EF model includes `lease_generation` for fencing, opaque payload text, and nullable `trace_context`. Pass `EfCoreStorageProvider.PostgreSql` to `GetModelBuilderConfiguration()` so the fluent model maps `trace_context` as optional `jsonb`. Add the new column in your application-owned EF migration before running the v6 store.

Add the column in your migration when upgrading from an older table that predates LiteBus v1 DDL:

```csharp
migrationBuilder.AddColumn<string>(
    name: "trace_context",
    schema: "public",
    table: "litebus_outbox_messages",
    type: "jsonb",
    nullable: true);
```

## Related Docs

- [Outbox](../reliable-messaging/outbox.md)
- [Hosted services](../architecture/hosted-services.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
- [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md)
