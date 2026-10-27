# PostgreSQL Idempotency Store

`LiteBus.Commands.Storage.PostgreSql` provides `PostgreSqlIdempotencyStore`, an `IIdempotencyStore` that remembers command idempotency keys in a PostgreSQL table so a key claimed on one host is visible to every other host. Use it when commands are mediated on more than one process and a repeat must be answered without running the work again.

For declaring keys and how the shortcut and completion handler settle them, see [In-Process Idempotency](../concepts/idempotency.md).

## Install

Reference `LiteBus.Commands` and `LiteBus.Commands.Storage.PostgreSql`. The storage package targets PostgreSQL through Npgsql and the shared PostgreSQL schema helpers.

## Register

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(ApplyPaymentCommand).Assembly);
        commands.EnableIdempotency();
        commands.UsePostgreSqlIdempotencyStore(pg =>
        {
            pg.UseConnectionString(connectionString);
            pg.UseOptions(new PostgreSqlIdempotencyStoreOptions
            {
                SchemaName = "public",
                TableName = "litebus_idempotency_keys"
            });
        });
    });
});
```

`UsePostgreSqlIdempotencyStore` registers the store as a child module of the command module, so it is composed inside `AddCommands` and cannot be registered without the axis whose shortcuts claim through it. `UseConnectionString` creates a data source the module owns and disposes with the container; `UseDataSource` uses an application-owned `NpgsqlDataSource` the module leaves alone. The module registers `IIdempotencyStore` as a singleton; an application that registers its own `IIdempotencyStore` should not add this module.

## Claim Behavior

A claim inserts the key with its fingerprint and lets the primary key refuse the duplicate. An insert that lands is granted. An insert refused by a row already marked applied reports the stored payload for the repeat, or a fingerprint mismatch when the stored fingerprint and the incoming one are both set and differ. An insert refused by a row still waiting to settle reports in-flight, and the pipeline denies the delivery with `idempotency.in-flight` so the caller retries after the first delivery settles.

Both shipped stores answer a held key the same way, because two deliveries racing across processes are the case idempotency exists to win. The in-memory store is still a test double: it forgets on restart and shares nothing across processes, so it cannot make a claim about the system.

## Claim Timeout

An unsettled claim blocks its key until `ClaimTimeout` passes, five minutes by default. A claimant that crashes without settling would otherwise block its key until an operator intervenes, so a claim older than the timeout is taken over: the new caller refreshes the row and is granted the key. The takeover compares the `claimed_at` value it read, so two callers racing to take over the same expired claim cannot both win. Keep the timeout comfortably longer than the slowest mediation that claims through the store, so a live claimant always settles first.

```csharp
pg.UseOptions(new PostgreSqlIdempotencyStoreOptions
{
    SchemaName = "public",
    TableName = "litebus_idempotency_keys",
    ClaimTimeout = TimeSpan.FromMinutes(10)
});
```

## Retention

Nothing expires an applied key. The store grows by one row per applied key until the application purges rows whose repeats stopped being plausible, using `completed_at` as the cutoff. A crashed claimant leaves its row unsettled rather than applied; that key is taken over once its claim outlasts `ClaimTimeout` instead of staying blocked.

## Schema Management

The table defaults to `public.litebus_idempotency_keys` with `idempotency_key` as the primary key, the recorded `payload`, the `fingerprint` the key was claimed with, the `is_completed` flag that separates an in-flight claim from an applied key, and `claimed_at` and `completed_at` timestamps. Schema creation follows the same three ownership models as the other PostgreSQL stores, and the `idempotency.postgresql.schema` probe reports whether the table matches the expected version. See [PostgreSQL Schema Management](postgresql-schema-management.md).

## Limitations

Each store call runs on its own connection, so the claim is not atomic with the change the handler applies. A crash between the claim commit and the handler commit leaves the key claimed for work that never happened, and only a mediation that ends inside the process settles its claim. Commands whose handler joins an application transaction should claim through that same unit of work with a store that can see it; this store suits commands applied without one. A concurrent claim is answered in-flight rather than waiting, so the caller owns the retry.

## Related Docs

- [In-Process Idempotency](../concepts/idempotency.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
