# PostgreSQL Schema Management

The PostgreSQL inbox and outbox stores need a small, stable set of tables and indexes. This page explains how those objects are created and validated, and how to choose between explicit bootstrap, opt-in host bootstrap, and migration-owned SQL files without tying LiteBus to a migration framework. It assumes you have set up a store from [Reliable Messaging](../reliable-messaging/README.md).

## Two Different Version Concepts

LiteBus uses two independent version numbers. Mixing them up is a common source of confusion.

| Version | Stored where | What it versions | Who changes it |
| --- | --- | --- | --- |
| **Contract version** | Each inbox/outbox row (`contract_version`) | JSON payload shape for one message type | Your application when you register `Contracts.Register<T>(name, version: 2)` |
| **Table schema version** | `litebus_schema_versions` metadata table | Physical columns and indexes for the store table | LiteBus when you call `EnsureAsync` or run published create scripts |

Contract version is per message type. Table schema version is per physical store table.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Storage.PostgreSql` | Shared SQL templates, identifier quoting, schema version metadata, advisory locks, logging hook, drift exceptions |
| `LiteBus.Inbox.Storage.PostgreSql` | Inbox store, `PostgreSqlInboxSchema` helper, inbox SQL files, optional `PostgreSqlInboxSchemaInitializer` |
| `LiteBus.Outbox.Storage.PostgreSql` | Outbox store, `PostgreSqlOutboxSchema` helper, outbox SQL files, optional `PostgreSqlOutboxSchemaInitializer` |

`LiteBus.Storage.PostgreSql` depends only on `Npgsql`.

## Canonical SQL Files (Recommended for Production)

LiteBus ships the DDL as plain `.sql` files. The runtime loads them as **embedded resources** inside the DLL (so `EnsureAsync` and `GetCreateScript` work from NuGet). The same files are also included as **loose files in the NuGet package** under `sql/` for copy-paste into migration tools.

### Copy from the Installed NuGet Package

After `dotnet add package LiteBus.Inbox.Storage.PostgreSql`, open the package folder in your NuGet cache:

```text
~/.nuget/packages/LiteBus.inbox.storage.postgresql/{version}/sql/inbox/v1/create.sql
~/.nuget/packages/LiteBus.inbox.storage.postgresql/{version}/sql/inbox/v1/ensure_indexes.sql
```

Shared metadata scripts ship in `LiteBus.Storage.PostgreSql`:

```text
~/.nuget/packages/LiteBus.storage.postgresql/{version}/sql/metadata/create.sql
```

On Windows the root is typically `%USERPROFILE%\.nuget\packages\`.

### Copy from the GitHub Repository

You can also copy from the LiteBus source tree (same file contents as the NuGet package):

| File | Purpose |
| --- | --- |
| `src/LiteBus.Storage.PostgreSql/Sql/metadata/create.sql` | Creates `litebus_schema_versions` |
| `src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/create.sql` | Creates the version 1 inbox table and indexes |
| `src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/ensure_indexes.sql` | Re-applies inbox indexes idempotently |
| `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v1/create.sql` | Creates the version 1 outbox table and indexes |
| `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v1/ensure_indexes.sql` | Re-applies outbox indexes idempotently |

Discover files from code as well:

```csharp
PostgreSqlSchemaSqlPaths.Files              // shared metadata SQL
PostgreSqlInboxSchema.SqlFiles              // inbox SQL catalog
PostgreSqlOutboxSchema.SqlFiles             // outbox SQL catalog

// Repository path constants:
PostgreSqlInboxSchemaSqlPaths.V1Create      // src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/create.sql
PostgreSqlOutboxSchemaSqlPaths.V1Create     // src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v1/create.sql
```

### Render Scripts in Code

When object names differ from LiteBus defaults, call `GetCreateScript` to render the embedded SQL templates with your options:

```csharp
var options = new PostgreSqlInboxStoreOptions
{
    SchemaName = "app",
    TableName = "litebus_inbox_messages"
};

var ddl = PostgreSqlInboxSchema.GetCreateScript(options);
File.WriteAllText("V001__litebus_inbox.sql", ddl);
```

Both approaches are valid. Copying the `.sql` files gives DBAs full control. Rendering with `GetCreateScript` avoids manual token replacement.

## Best Practices for External SQL Resources

LiteBus ships SQL as plain files that are compiled into the assembly and also copied into the NuGet package. Follow these practices when you own migrations or call schema helpers in production.

### Single Source of Truth

Edit the `.sql` files under each package's `Sql/` folder in the LiteBus repository. Do not maintain a forked copy with different DDL unless your DBAs require it. When you need custom object names, keep the canonical scripts and render them with `GetCreateScript` rather than hand-editing placeholders.

### Embedded vs Loose Files

| Location | When it is used | Best practice |
| --- | --- | --- |
| Embedded resource in the DLL | `EnsureAsync`, `GetCreateScript`, integration tests | Trust this at runtime; it always matches the package version you referenced |
| Loose `sql/` folder in the NuGet package | DBA review, Flyway/Liquibase copy-paste | Copy verbatim into your migration repo; pin the LiteBus package version in release notes |
| GitHub `src/.../Sql/` tree | Documentation and PR review | Same content as the NuGet loose files for a given release tag |

Runtime code never reads loose files from disk. Deployments that omit the `sql/` folder from the published app still work because the embedded copy is always present.

### Token Replacement

Placeholders use the form `{{TokenName}}`. Prefer `GetCreateScript(options)` over manual search-and-replace so schema names, table names, and index names stay correctly quoted for PostgreSQL.

### Connection Configuration

| Approach | When to use |
| --- | --- |
| `UseDataSource(NpgsqlDataSource)` | Production apps that already build one shared data source for pooling, health checks, and tracing |
| `UseConnectionString(string)` | Samples, tests, and small services where the module should create and register the data source |

When inbox and outbox share one database, build a single `NpgsqlDataSource` and pass it to both stores with `UseDataSource`. Calling `UseConnectionString` on both stores creates two pools against the same server.

```csharp
var dataSource = NpgsqlDataSource.Create(configuration.GetConnectionString("OrdersDb")!);

builder.AddInbox(inbox =>
    inbox.UsePostgreSqlStorage(p => p.UseDataSource(dataSource)));

builder.AddOutbox(outbox =>
    outbox.UsePostgreSqlStorage(p => p.UseDataSource(dataSource)));
```

### Version Alignment

v6 ships **schema version 1** only. There is no multi-version upgrade loop. Record the LiteBus package version and `CurrentSchemaVersion = 1` in your deployment runbook. When adopting v6 on an existing database, drop legacy LiteBus tables and apply v1 create scripts; do not expect in-place migration from v5 shapes.

### Do Not Edit Embedded Resource Names Casually

If you contribute to LiteBus, keep SQL paths stable. The loader resolves `{AssemblyName}.Sql.{path.with.dots}.sql`. Renaming folders without updating `EmbeddedResource` items breaks runtime schema creation.

### Placeholder Tokens

| Token | Example rendered value | Used in |
| --- | --- | --- |
| `{{QuotedSchemaName}}` | `"app"` | Store table create scripts |
| `{{QualifiedTableName}}` | `"app"."litebus_inbox_messages"` | Store DDL and inbox/outbox upgrade scripts |
| `{{QuotedMetadataSchemaName}}` | `"app"` | Metadata table create |
| `{{QualifiedMetadataTableName}}` | `"app"."litebus_schema_versions"` | Metadata DDL |
| `{{IdempotencyIndexName}}` | quoted index name | Inbox only |
| `{{LeaseIndexName}}` | quoted index name | Inbox and outbox |
| `{{TopicIndexName}}` | quoted index name | Outbox only |

## Three Ownership Models

Pick one model per environment. You can use different models in development and production.

### 1. Migration-Owned (Recommended for Production)

Your team owns schema timing. Copy the canonical `.sql` files or rendered output from `GetCreateScript`.

Leave `EnsureSchemaCreationOnStartup = false` (the default). Run validation in deploy checks if you want a fail-fast guard:

```csharp
await PostgreSqlInboxSchema.ValidateAsync(dataSource, options, cancellationToken);
```

### 2. Explicit Bootstrap

Call `EnsureAsync` from application startup, a one-shot deploy job, or integration test setup. The call is idempotent.

```csharp
await PostgreSqlInboxSchema.EnsureAsync(dataSource, options, cancellationToken);
await PostgreSqlOutboxSchema.EnsureAsync(dataSource, options, cancellationToken);
```

Use this for internal services, prototypes, and test environments where a migration pipeline is overhead.

### 3. Opt-in Host Schema Creation

Enable automatic schema creation when the generic host starts. Call `EnsureSchemaCreationOnStartup()` on the nested PostgreSQL storage builder. `UsePostgreSqlStorage` registers `PostgreSqlInboxSchemaInitializer` as `IStartupTask` when `EnableSchemaInitialization` is enabled (the default).

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<MyCommand>("my.command", 1);
        inbox.UsePostgreSqlStorage(postgres =>
        {
            postgres.UseDataSource(dataSource);
            postgres.EnsureSchemaCreationOnStartup();
        });
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

When `EnsureSchemaCreationOnStartup` is `false`, the initializer skips `EnsureAsync` but still runs `ValidateAsync` when `ValidateSchemaCreationOnStartup` is `true`. Use `postgres.DisableSchemaInitialization()` when you do not want the hosted initializer at all.

## Configuration Options

Both inbox and outbox share the same option shape through `PostgreSqlSchemaStoreOptions`.

| Option | Default | Purpose |
| --- | --- | --- |
| `SchemaName` | `public` | PostgreSQL schema for the store table |
| `TableName` | `litebus_inbox_messages` / `litebus_outbox_messages` | Store table name |
| `MetadataSchemaName` | `public` | Schema for the version metadata table |
| `MetadataTableName` | `litebus_schema_versions` | Version metadata table name |
| `EnsureSchemaCreationOnStartup` | `false` | Run `EnsureAsync` inside `PostgreSql*SchemaInitializer` when the host starts |
| `ValidateSchemaCreationOnStartup` | `true` | Run `ValidateAsync` in the initializer when enabled, including validate-only when ensure is `false` |
| `ValidateIndexesOnStartup` | `true` | When validation runs, also verify required store indexes exist |
| `Logger` | `null` | Optional `IPostgreSqlSchemaLogger` for schema operations |

When `ValidateSchemaCreationOnStartup` is `true`, the initializer runs `ValidateAsync` even if `EnsureSchemaCreationOnStartup` is `false` (validate-only startup). When both are enabled, ensure runs first, then validation.

Example with custom object names and logging:

```csharp
postgres.UseOptions(new PostgreSqlInboxStoreOptions
{
    SchemaName = "messaging",
    TableName = "app_inbox",
    MetadataSchemaName = "messaging",
    MetadataTableName = "litebus_schema_versions",
    Logger = new ConsolePostgreSqlSchemaLogger()
});
```

Use the same option instance for the store registration and schema helpers so table names stay aligned.

## Logging

`LiteBus.Storage.PostgreSql` exposes `IPostgreSqlSchemaLogger` without taking a dependency on Microsoft logging packages. Assign an implementation through `PostgreSqlSchemaStoreOptions.Logger`.

Schema operations log at these points:

- Starting and completing `EnsureAsync`
- Advisory lock acquired or waiting on another session
- Creating version 1 objects
- Applying version upgrades
- Recording metadata version rows
- Validation success or `PostgreSqlSchemaDriftException` details

Example adapter for local development:

```csharp
public sealed class ConsolePostgreSqlSchemaLogger : IPostgreSqlSchemaLogger
{
    public void Log(PostgreSqlSchemaLogLevel level, string message, Exception? exception = null)
    {
        Console.WriteLine($"[{level}] {message}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }
}
```

Hosting applications can bridge `IPostgreSqlSchemaLogger` to `ILogger<T>` in the hosting package without changing `LiteBus.Storage.PostgreSql` dependencies.

## Schema Version Metadata

LiteBus records applied table schema versions in `litebus_schema_versions`:

| Column | Purpose |
| --- | --- |
| `component` | `inbox` or `outbox` |
| `schema_name` | Store table schema |
| `table_name` | Store table name |
| `version` | Applied table schema version |
| `applied_at` | UTC timestamp of the last recorded upgrade |

Primary key: `(component, schema_name, table_name)`.

One metadata table serves all LiteBus store tables in the database. Each inbox or outbox table you configure gets its own row.

## Current Schema Versions

| Component | Version | Notes |
| --- | --- | --- |
| Inbox | **1** | Full column set including `trace_context`, terminal timestamps, operational history columns, required indexes, and optional insert notify trigger |
| Outbox | **1** | Full column set including `idempotency_key`, `trace_context`, terminal timestamps, operational history columns, required indexes, and optional insert notify trigger |

Constants: `PostgreSqlInboxSchema.CurrentSchemaVersion` and `PostgreSqlOutboxSchema.CurrentSchemaVersion`.

The Npgsql store implementations do not read `trace_context` yet. The column exists so future LiteBus releases can add tracing metadata without another immediate breaking DDL change.

### Indexes: Ensure vs Validate

`EnsureAsync` runs the idempotent `ensure_indexes` script after create so required indexes exist. `ValidateAsync` checks that the table exists, required columns for `CurrentSchemaVersion` are present, required indexes exist, and metadata (or inferred column shape) matches version **1**.

## API Reference

### Create and Validate

| Method | Purpose |
| --- | --- |
| `GetCreateScript(options)` | Full rendered DDL for schema version 1, metadata table, indexes, and notify trigger |
| `EnsureAsync(dataSource, options, ct)` | Create schema version 1 and record metadata idempotently |
| `CreateIfNotExistsAsync(...)` | Alias for `EnsureAsync` kept for readability in tests |
| `ValidateAsync(dataSource, options, ct)` | Fail fast when the physical table, indexes, or metadata does not match the library |
| `SqlFiles` | Catalog of repository SQL file paths and descriptions |

### Exceptions

`PostgreSqlSchemaDriftException` is thrown by `ValidateAsync` and by startup validation. It includes:

- `Component` (`inbox` / `outbox`)
- `SchemaName`, `TableName`
- `ExpectedVersion`, `ActualVersion`
- `Details` (missing columns, missing table, version mismatch)

Treat this as a deployment blocker. Do not catch and ignore it in production startup.

## Multi-Instance and Microservice Safety

`EnsureAsync` is safe when many pods or services start at the same time.

1. **Initial create** uses `CREATE ... IF NOT EXISTS` for schemas, tables, and indexes.
2. **Advisory locks** ensure one connection creates the schema at a time per store table. Other instances wait until the table reaches the expected version or the wait timeout elapses.

Lock key format: `litebus:{component}:{schema}:{table}`.

Waiting instances poll the metadata table and physical column shape. They do not fail merely because another instance holds the lock.

### What to Run from Every Pod vs One Deploy Job

| Operation | Safe from all pods | Recommended pattern |
| --- | --- | --- |
| First-time `EnsureAsync` | Yes | Opt-in host schema creation or startup call |
| `ValidateAsync` | Yes | Deploy check or startup when `ValidateSchemaCreationOnStartup = true` |

## Existing Databases

LiteBus v6 does not upgrade pre-v6 store tables. Drop and recreate the inbox/outbox tables, or apply `GetCreateScript()` through your migration pipeline before deploying application code that expects schema version **1**.

## Registration Order with Background Services

Register storage and dispatchers before processor modules so dependencies resolve during module build:

```csharp
builder.AddInbox(inbox =>
{
    inbox.UsePostgreSqlStorage(pg => pg.EnsureSchemaCreationOnStartup());
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();
});

builder.AddOutbox(outbox =>
{
    outbox.UsePostgreSqlStorage(pg => pg.EnsureSchemaCreationOnStartup());
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

Schema initializers implement `IStartupTask` and run sequentially before processor loops. Register storage modules before inbox or outbox modules so schema ensure/validate completes first. See [Hosted services](../architecture/hosted-services.md).

## Entity Framework Core Storage

LiteBus ships EF Core inbox and outbox stores for applications that already use `DbContext` and migrations. EF apps own all DDL; LiteBus does not register `PostgreSql*SchemaInitializer` for EF storage.

| Topic | PostgreSQL Npgsql store | EF Core store |
| --- | --- | --- |
| Schema ownership | Migration-owned SQL, `EnsureAsync`, or opt-in host bootstrap | Application EF migrations |
| Default table | `public.litebus_inbox_messages` / `public.litebus_outbox_messages` | Same defaults via `EntityFrameworkCoreInboxStoreOptions` / `EntityFrameworkCoreOutboxStoreOptions` |
| Column mapping | Canonical `.sql` files | `GetModelBuilderConfiguration()` on `InboxMessageEntity` / `OutboxMessageEntity` |
| Table schema version | Inbox **1** / outbox **1** | Align EF migrations with PostgreSQL v1 columns when sharing a database with Npgsql stores |

The built-in EF fluent configuration maps the same snake_case column names as the PostgreSQL scripts. Pass `EfCoreStorageProvider.PostgreSql` to `GetModelBuilderConfiguration()` to map opaque `payload` text and optional `trace_context` as `jsonb` on PostgreSQL. Other providers keep payload text and select their trace metadata type (see [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md)).

When using EF Core storage, pass the same `SchemaName` and `TableName` to `EntityFrameworkCoreInboxStoreOptions` / `EntityFrameworkCoreOutboxStoreOptions` and to `GetModelBuilderConfiguration()` so lease SQL and migrations target the same table.

See [Inbox Entity Framework Core Storage](inbox-ef-core-storage.md) and [Outbox Entity Framework Core Storage](outbox-ef-core-storage.md).

## Manual Deploy Job Example

For teams that want automatic DDL without running it from every pod:

```csharp
var dataSource = NpgsqlDataSource.Create(connectionString);

await PostgreSqlInboxSchema.EnsureAsync(dataSource, inboxOptions);
await PostgreSqlOutboxSchema.EnsureAsync(dataSource, outboxOptions);

await PostgreSqlInboxSchema.ValidateAsync(dataSource, inboxOptions);
await PostgreSqlOutboxSchema.ValidateAsync(dataSource, outboxOptions);

Console.WriteLine("LiteBus PostgreSQL schema is ready.");
```

Run this once per deployment before rolling out application pods with `EnsureSchemaCreationOnStartup = false`.

## Flyway / Liquibase Workflow

1. Copy the `.sql` files from `PostgreSqlInboxSchema.SqlFiles` / `PostgreSqlOutboxSchema.SqlFiles` into your migration repository, or generate rendered scripts with `GetCreateScript`.
2. Record the LiteBus release and schema version **1** in your internal runbook.
3. Call `ValidateAsync` from a smoke test after migration.

Prefer copying the shipped `.sql` files verbatim. Edit only when DBAs require renames, tablespaces, or ownership clauses.

## Custom Stores

If you implement `IInboxStore` / `IOutboxStore` and your own storage, you own the schema entirely. LiteBus does not require the metadata table or `trace_context` column for custom stores. Match the envelope fields your implementation reads and writes.

## Future LiteBus Releases

When LiteBus changes the store table shape, expect a new major release with a fresh schema version and new create scripts. Apply the published `GetCreateScript()` output through your migration pipeline; LiteBus does not ship incremental upgrade scripts.

LiteBus will not auto-run destructive changes (drops, renames, narrowing type changes) from application pods.

## Related Docs

- [Inbox](../reliable-messaging/inbox.md)
- [Outbox](../reliable-messaging/outbox.md)
- [Hosted services](../architecture/hosted-services.md)
- [Dependency Graph](../architecture/dependency-graph.md)
- [Migration Guide v5](../migration/v5.md)

## Next

Read [Hosted services](../architecture/hosted-services.md) to run the background workers that drain these tables.
