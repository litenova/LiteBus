# Marten Unit of Work, Idempotency, and Audit

`LiteBus.Commands.Storage.Marten` binds command mediation to the Marten session the handler already writes through: `MartenUnitOfWork` commits the scoped `IDocumentSession` when the mediation succeeds, `MartenIdempotencyStore` remembers idempotency keys as Marten documents, and `MartenAuditTrail` writes audit records as Marten documents. Use it when command handlers stage Marten documents and the commit boundary should be the session save.

For declaring idempotency keys and how the shortcut and completion handler settle them, see [In-Process Idempotency](../concepts/idempotency.md). For what an audit record contains, see [Auditing](../concepts/auditing.md).

## Install

Reference `LiteBus.Commands` and `LiteBus.Commands.Storage.Marten`. Your application already references Marten for `AddMarten`; the storage package resolves the application-owned `IDocumentStore` and the scoped `IDocumentSession` from the container.

## Register

```csharp
services.AddMarten(options =>
{
    options.Connection(connectionString);
});

services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging =>
    {
        messaging.UseAuditTrail<MartenAuditTrail>();
    });
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
        commands.EnableAuditing();
        commands.EnableIdempotency();
        commands.UseMartenUnitOfWork();
        commands.UseMartenIdempotencyStore(marten =>
        {
            marten.ClaimTimeout = TimeSpan.FromMinutes(10);
        });
    });
});
```

LiteBus does not register Marten: the application calls `AddMarten`, and the unit of work and the trail resolve the scoped `IDocumentSession` from the current scope. `UseMartenIdempotencyStore` registers the store as a child module of the command module, so it is composed inside `AddCommands` and cannot be registered without the axis whose shortcuts claim through it. The module registers `IIdempotencyStore` as a singleton; an application that registers its own `IIdempotencyStore` should not add this module.

## Unit of Work

`UseMartenUnitOfWork` resolves `MartenUnitOfWork` scoped and commits the session only when the mediation completed successfully. A denied, invalid, failed, or cancelled mediation disposes the session without saving, so staged documents roll back by construction. The handler, the guards, and the completion handlers resolve the same scoped session, so everything staged during the mediation commits in one `SaveChangesAsync`.

Query mediations commit the same session through the query builder: call its `UseUnitOfWork` hook with `MartenUnitOfWork` directly. This package ships no query builder extension, so a command storage package does not pull the query axis onto its consumers.

## Idempotency Claims

A claim opens a short serializable session from `IDocumentStore` and commits it before returning, so a granted claim is visible to a concurrent claimant immediately. A new key is granted with a claim token fenced into the document. An applied key replays the stored payload when the fingerprint matches or either side supplied none, and reports `FingerprintMismatch` when both sides supplied fingerprints that differ. An unsettled claim whose age is within `ClaimTimeout` reports `InFlight`. An unsettled claim older than `ClaimTimeout` is taken over: the new claimant is granted with a new token and the previous fingerprint and timestamp are replaced. A completion or release lands only while the document still carries the token the caller was granted: a settle that arrives after a takeover is a no-op, and completing a key the caller does not hold stores nothing. `ClaimTimeout` defaults to five minutes, must be positive, and is measured on the messaging `TimeProvider`, so `UseTimeProvider` drives deterministic tests.

Two concurrent claims never both grant. A new-key race fails the loser with a unique violation and a takeover race with a serialization failure, and each is retried against the committed winner inside the store. The document identity is the key, so the race is always over one row.

## Audit Trail

A record for a successful mediation is staged on the scoped session and committed by the same `SaveChangesAsync` that commits the handler change, so the record and the change are atomic. The ordering is fixed by priority: the audit completion handler runs at `Observability` and the unit-of-work commit runs at `UnitOfWork`, and handlers run in ascending priority order, so the writer always stages before the commit.

A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the unit of work rolls the session back on exactly those paths. Those records are written out of band on a short session that saves immediately, so a refusal or a fault is still recorded when the change it describes never commits.

## Limitations

Each idempotency call runs on its own session, so the claim is not atomic with the change the handler applies. A crash between the claim commit and the handler commit leaves the key claimed for work that never happened, and only a mediation that ends inside the process settles its claim. Keep `ClaimTimeout` longer than the longest mediation that claims through the store: a slow claimant can still lose its key to a takeover, but its late settle is then a no-op while the new claimant's work runs on. Release only deletes a claim that is still unsettled when its session commits, so a release racing a completion cannot reopen an applied key. Success audit records share the handler transaction and roll back with it; stage them only through the completion-stage writer, which runs before the commit. Marten must target the database the documents live in. Marten assemblies are not strong-named.

## Related Docs

- [In-Process Idempotency](../concepts/idempotency.md)
- [Auditing](../concepts/auditing.md)
- [PostgreSQL Idempotency Store](commands-postgresql-idempotency-store.md)
- [Outbox Marten Storage](outbox-marten-storage.md)
