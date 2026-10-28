# Inbox Entity Framework Core Storage

`LiteBus.Inbox.Storage.EntityFrameworkCore` provides `EfCoreInboxStore`, which implements `IInboxStore`, `IInboxLeaseStore`, and `IInboxStateWriter` on a single `DbContext`. Use it when the application already owns a `DbContext` and EF migrations.

For EF transactional accept (`ITransactionalInbox<TContext>`), see [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md).

## Schema Ownership

LiteBus does not create or upgrade inbox tables for EF Core. Your application owns migrations and deployment timing. Apply `InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration()` in `OnModelCreating` so column names and indexes match what `EfCoreInboxStore` expects. There is no public per-entity configuration entry point.

For PostgreSQL-backed apps that use raw Npgsql inbox storage instead, see [PostgreSQL Schema Management](postgresql-schema-management.md).

## Registration

```csharp
services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<PlaceOrder>("orders.place", 1);
        inbox.UseEntityFrameworkCoreStorage(options => options.UseDbContext<AppDbContext>());
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

`AppDbContext` must implement `IInboxDbContext` and expose `DbSet<InboxMessageEntity> InboxMessages`. The adapter requires `IDbContextFactory<AppDbContext>`, creates one context per store operation, and disposes it after the operation. It does not create a dependency injection scope inside the store.

## Default Table and Columns

| Setting | Default |
| --- | --- |
| Schema | `public` |
| Table | `litebus_inbox_messages` |

`GetModelBuilderConfiguration()` maps entity properties to the same snake_case columns as the PostgreSQL inbox scripts:

| Column | Role |
| --- | --- |
| `message_id` | Primary key (`Guid`, application-assigned) |
| `contract_name`, `contract_version` | Stable message contract |
| `payload` | JSON serialized message (store type depends on provider; see below) |
| `created_at`, `visible_after`, `attempt_count`, `status` | Scheduling and lifecycle |
| `idempotency_key` | Optional deduplication scoped per `tenant_id` (unique composite index with `IS NOT NULL` filter) |
| `lease_owner`, `lease_expires_at` | Processor leasing |
| `lease_generation` | Fencing counter incremented on each lease |
| `last_error`, `error_type` | Last failure message and the exception type full name (or type name when the full name is unavailable) |
| `completed_at`, `last_attempted_at`, `first_failed_at`, `dead_lettered_at` | Forensic timestamps for terminal state, last attempt, first failure, and dead-lettering |
| `last_lease_owner` | Owner of the most recent expired or completed lease |
| `correlation_id`, `causation_id`, `tenant_id` | Optional metadata |
| `trace_context` | Optional distributed trace payload (`jsonb` on PostgreSQL) |

Operators query `last_error`, `error_type`, `last_lease_owner`, and the forensic timestamps. Processors use `status`, `visible_after`, `lease_expires_at`, and `lease_generation`.

The indexes are a unique filtered index on `(tenant_id, idempotency_key)`, a composite index on `(status, visible_after, lease_expires_at, created_at)` for eligibility, and a chronological index `IX_LiteBus_Inbox_CreatedAt` for ordered lease scans. Null or whitespace `tenant_id` values normalize to an empty string for idempotency scope.

Override names with `EntityFrameworkCoreInboxStoreOptions`:

```csharp
builder.AddInbox(inbox =>
{
    inbox.UseEntityFrameworkCoreStorage(options =>
    {
        options.UseDbContext<AppDbContext>();
        options.UseOptions(new EntityFrameworkCoreInboxStoreOptions
        {
            SchemaName = "messaging",
            TableName = "litebus_inbox_messages"
        });
    });
});
```

## Align Store Options with Your EF Model

`EfCoreInboxStore` uses `EntityFrameworkCoreInboxStoreOptions` for schema and table names when it runs raw lease SQL. Pass the **same** `SchemaName` and `TableName` to `GetModelBuilderConfiguration()` in `OnModelCreating` so EF migrations target the table the store reads and writes.

If the two configurations diverge, inserts and state updates can succeed against one table while leasing queries another, or lease SQL can reference column names that do not exist in your migration. When you override table or schema names, set both `UseOptions(...)` on `UseEntityFrameworkCoreStorage` and the options argument to `GetModelBuilderConfiguration()`.

When inference from the active `DbContext` is unreliable (for example SQL Server with a non-default schema), also set `LeaseProvider` on `EntityFrameworkCoreInboxStoreOptions` to match your database.

## Provider-Specific Model Configuration

Pass an `EfCoreStorageProvider` value to `GetModelBuilderConfiguration()` so payload and trace columns use the correct store types:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var inboxOptions = new EntityFrameworkCoreInboxStoreOptions();

    if (Database.IsNpgsql())
    {
        modelBuilder.GetModelBuilderConfiguration(inboxOptions, EfCoreStorageProvider.PostgreSql);
        return;
    }

    if (Database.IsSqlServer())
    {
        inboxOptions.SchemaName = "dbo";
        modelBuilder.GetModelBuilderConfiguration(inboxOptions, EfCoreStorageProvider.SqlServer);
        return;
    }

    modelBuilder.GetModelBuilderConfiguration(inboxOptions);
}
```

| Provider | EF package (application reference) | Default schema | Payload column type |
| --- | --- | --- | --- |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | `public` | `TEXT` |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | `dbo` (set explicitly) | `TEXT` |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | Current connection database | `TEXT` |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | None | `TEXT` |

When the provider argument is omitted, LiteBus leaves payload and trace columns for application-owned fluent configuration. PostgreSQL tables created from LiteBus scripts use `text` for `payload` and `jsonb` for `trace_context`. The EF model must use the same types when it shares those tables.

Set an optional lease override when inference from the active `DbContext` is not enough:

```csharp
options.UseOptions(new EntityFrameworkCoreInboxStoreOptions
{
    LeaseProvider = EfCoreStorageProvider.SqlServer
});
```

## Idempotency on the EF Transactional Path

`EfCoreInboxStore` and `ITransactionalInbox<TContext>` honor `Idempotency.Keyed` on accept metadata:

| `IdempotencyConflictMode` | Behavior on duplicate key in one transaction |
| --- | --- |
| `ReturnExisting` (default) | Returns the existing receipt with `InboxAcceptOutcome.AlreadyAccepted`. |
| `Strict` | Throws `IdempotencyConflictException` before `SaveChanges`, but only when the conflicting row differs. A byte-identical replay returns the existing receipt. |

`Strict` compares message id, contract name, contract version, payload, idempotency key, correlation id, causation id, and tenant id. Rows that match on all eight fields return the stored receipt. Any difference throws.

The `LiteBusInboxSaveChangesInterceptor` path does not deduplicate at accept time. A duplicate `(tenant_id, idempotency_key)` pair in the same `SaveChanges` batch surfaces as a `DbUpdateException` from the unique index and aborts the caller transaction.

## Alignment with PostgreSQL `CurrentSchemaVersion`

`PostgreSqlInboxSchema.CurrentSchemaVersion` is **1**. The EF model includes `lease_generation` for fencing, opaque payload text, and nullable `trace_context`. Pass `EfCoreStorageProvider.PostgreSql` to `GetModelBuilderConfiguration()` so the fluent model maps `trace_context` as optional `jsonb` on PostgreSQL. Generate an application-owned migration from the complete current model.

Generate the complete current model into a new table when an existing table has an incompatible shape. Follow the row-mapping checks in the [Migration Guide](../migration/v6.md) when historical data must be retained.

## Processing Notes

`EfCoreInboxStore` uses EF for writes and state updates. Leasing uses provider-specific skip-locked SQL for PostgreSQL (`FOR UPDATE SKIP LOCKED`), SQL Server (`UPDLOCK`, `READPAST`, `ROWLOCK`), and MySQL (`FOR UPDATE SKIP LOCKED` inside a `READ COMMITTED` transaction). MySQL migrations must include `IX_LiteBus_Inbox_CreatedAt`. The lease query selects this index to avoid range locks and deadlocks during ordered concurrent claims.

SQLite is supported through a file-backed database. Model configuration stores every durable `DateTimeOffset` as UTC ticks so eligibility filters and chronological queries remain server-side. SQLite allows one writer at a time, so the store wraps each claim in a serializable transaction. Use PostgreSQL, SQL Server, or MySQL when concurrent write throughput exceeds that constraint.

The in-memory EF provider uses a process lock for unit tests.

Register storage and a dispatcher before `EnableInboxProcessor()`. Module build fails when the processor is enabled without a dispatcher. Schema initializers from the PostgreSQL package do not apply to EF storage.

## Related Docs

- [Inbox](../reliable-messaging/inbox.md)
- [Hosted Services](../architecture/hosted-services.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
- [Outbox Entity Framework Core Storage](outbox-ef-core-storage.md)
