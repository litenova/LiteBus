# Inbox Entity Framework Core Storage



`LiteBus.Inbox.Storage.EntityFrameworkCore` provides `EfCoreInboxStore`, which implements `IInboxStore`, `IInboxLeaseStore`, and `IInboxStateWriter` on a single `DbContext`.

For EF transactional accept (`ITransactionalInbox<TContext>`), see [Transactional messaging writes](../reliable-messaging/transactional-writes.md).



## Schema Ownership



LiteBus does not create or upgrade inbox tables for EF Core. Your application owns migrations and deployment timing. Use `InboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration()` in `OnModelCreating` (or an `IEntityTypeConfiguration<InboxMessageEntity>`) so column names and indexes match what `EfCoreInboxStore` expects.



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

| `last_error` | Last failure message |

| `correlation_id`, `causation_id`, `tenant_id` | Optional metadata |



Indexes: unique filtered index on `(tenant_id, idempotency_key)`; composite index on `(status, visible_after, lease_expires_at, created_at)` for eligibility; chronological index `IX_LiteBus_Inbox_CreatedAt` for ordered lease scans. Null or whitespace `tenant_id` values normalize to an empty string for idempotency scope.



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



When the provider argument is omitted, LiteBus leaves payload and trace columns for application-owned fluent configuration. PostgreSQL tables created from LiteBus scripts use `text` for `payload` and `jsonb` for `trace_context`; the EF model must use the same types when it shares those tables.



Optional lease override when inference from the active `DbContext` is not enough:



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
| `Strict` | Throws `IdempotencyConflictException` before `SaveChanges`. |

The `LiteBusInboxSaveChangesInterceptor` path does not dedupe at accept time. A duplicate `(tenant_id, idempotency_key)` pair in the same `SaveChanges` batch surfaces as a `DbUpdateException` from the unique index and aborts the caller transaction.

## Alignment with PostgreSQL `CurrentSchemaVersion`

`PostgreSqlInboxSchema.CurrentSchemaVersion` is **3**. The EF model includes `lease_generation` for fencing, opaque payload text, and nullable `trace_context`. Pass `EfCoreStorageProvider.PostgreSql` to `GetModelBuilderConfiguration()` so the fluent model maps `trace_context` as optional `jsonb` on PostgreSQL. Add the new column in your application-owned EF migration before running the v6 store.

If you created migrations before this column existed, add it in a new migration (same shape as `add_trace_context_column.sql`):



```csharp

migrationBuilder.AddColumn<string>(

    name: "trace_context",

    schema: "public",

    table: "litebus_inbox_messages",

    type: "jsonb",

    nullable: true);

```



Use a `jsonb` column type on PostgreSQL. The column can remain unused until a future LiteBus release consumes it.



## Processing Notes



`EfCoreInboxStore` uses EF for writes and state updates. Leasing uses provider-specific skip-locked SQL for PostgreSQL (`FOR UPDATE SKIP LOCKED`), SQL Server (`UPDLOCK`, `READPAST`, `ROWLOCK`), and MySQL (`FOR UPDATE SKIP LOCKED` inside a `READ COMMITTED` transaction). MySQL migrations must include `IX_LiteBus_Inbox_CreatedAt`; the lease query selects this index to avoid range locks and deadlocks during ordered concurrent claims.

SQLite is supported through a file-backed database. Model configuration stores every durable `DateTimeOffset` as UTC ticks so eligibility filters and chronological queries remain server-side. SQLite allows one writer at a time, so the store wraps each claim in a serializable transaction. Use PostgreSQL, SQL Server, or MySQL when concurrent write throughput exceeds that constraint.

The in-memory EF provider uses a process lock for unit tests.



Register dispatchers before `EnableInboxProcessor()`. Schema initializers from the PostgreSQL package do not apply to EF storage.



## Related Docs



- [Inbox](../reliable-messaging/inbox.md)

- [Hosted services](../architecture/hosted-services.md)

- [PostgreSQL Schema Management](postgresql-schema-management.md)

- [Outbox Entity Framework Core Storage](outbox-ef-core-storage.md)
