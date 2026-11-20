# Marten Unit of Work and Idempotency

`LiteBus.Commands.Storage.Marten` binds command mediation to the Marten session the handler already writes through. `MartenUnitOfWork` commits the scoped `IDocumentSession` when the mediation succeeds, `MartenIdempotencyStore` remembers idempotency keys as Marten documents, and `MartenTenantSessionFactory` opens sessions for the tenant the current mediation runs in. Use it when command handlers stage Marten documents and the commit boundary should be the session save.

For declaring idempotency keys and how the shortcut and completion handler settle them, see [In-Process Idempotency](../concepts/idempotency.md). For audit records stored as Marten documents, see [Marten Audit Storage](auditing-marten.md).

## Install

Reference `LiteBus.Commands` and `LiteBus.Commands.Storage.Marten`. Your application already references Marten for `AddMarten`. The storage package resolves the application-owned `IDocumentStore` and the scoped `IDocumentSession` from the container.

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
        messaging.UseFaultRefusalClassifier<MartenConcurrencyRefusalClassifier>();
    });
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
        commands.EnableIdempotency();
        commands.UseMartenUnitOfWork();
        commands.UseMartenIdempotencyStore(marten =>
        {
            marten.ClaimTimeout = TimeSpan.FromMinutes(10);
        });
        commands.UseMartenTenantSessions();
    });
    builder.AddAuditing(auditing => auditing.UseMartenStore());
});
```

LiteBus does not register Marten. The application calls `AddMarten`, and the unit of work and the trail resolve the scoped `IDocumentSession` from the current scope. `UseMartenIdempotencyStore` registers the store as a child module of the command module, so it is composed inside `AddCommands` and cannot be registered without the axis whose shortcuts claim through it. The module registers `IIdempotencyStore` as a singleton. An application that registers its own `IIdempotencyStore` should not add this module.

## Unit of Work

`UseMartenUnitOfWork` registers `MartenUnitOfWork` with a scoped lifetime and commits the session only when the mediation completed successfully. A denied, invalid, failed, or cancelled mediation disposes the session without saving, so staged documents roll back. The handler, the guards, and the completion handlers resolve the same scoped session, so everything staged during the mediation commits in one `SaveChangesAsync`.

Query mediations commit the same session through the query builder. Call its `UseUnitOfWork` hook with `MartenUnitOfWork` directly. This package ships no query builder extension, so a command storage package does not pull the query axis onto its consumers.

## Idempotency Claims

A claim opens a short serializable session from `IDocumentStore` and commits it before returning, so a granted claim is visible to a concurrent claimant immediately. A new key is granted with a claim token fenced into the document. An applied key replays the stored payload when the fingerprint matches or either side supplied none. It reports `FingerprintMismatch` when both sides supplied fingerprints that differ.

An unsettled claim whose age is within `ClaimTimeout` reports `InFlight`. An unsettled claim older than `ClaimTimeout` is taken over. The new claimant is granted with a new token, and the previous fingerprint and timestamp are replaced. A completion or release lands only while the document still carries the token the caller was granted. A settle that arrives after a takeover is a no-op, and completing a key the caller does not hold stores nothing. `ClaimTimeout` defaults to five minutes, must be positive, and is measured on the messaging `TimeProvider`, so `UseTimeProvider` drives deterministic tests.

Two concurrent claims never both grant. A new-key race fails the loser with a unique violation, and a takeover race fails the loser with a serialization failure. Each is retried against the committed winner inside the store. The document identity is the key, so the race is always over one row.

## Audit Records

Audit records leave this package for `LiteBus.Auditing.Storage.Marten`, because queries and events are audited as well as commands. `AddAuditing` selects the audited axes with `ForCommands()` and stores the records with `UseMartenStore()`:

```csharp
services.AddLiteBus(builder =>
{
    builder.AddCommands(commands =>
    {
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
        commands.UseMartenUnitOfWork();
    });
    builder.Modules.AddAuditing(auditing => auditing
        .ForCommands()
        .UseMartenStore()
        .UseHashChain(chain => chain
            .CheckpointWhen(CheckpointPolicies.EveryRecords(100))));
});
```

A record for a successful mediation is staged on the scoped session and committed by the same `SaveChangesAsync` that commits the handler change, so the record and the change are atomic. The ordering is fixed by priority. The audit completion handler runs at `Observability` and the unit-of-work commit runs at `UnitOfWork`, and handlers run in ascending priority order, so the writer always stages before the commit.

A record for a denial, failure, cancellation, or invalid message cannot join that transaction, because the unit of work rolls the session back on exactly those paths. Those records are written on a separate short session that saves immediately, so a refusal or a fault is still recorded when the change it describes never commits. See [Marten Audit Storage](auditing-marten.md) for the trail, the chain defaults, and the `IAuditChain` operations.

## Tenant Sessions

`UseMartenTenantSessions` registers `MartenTenantSessionFactory` as a child module of the command module, so it is composed inside `AddCommands` and cannot be registered without the axis whose handlers open sessions through it. The factory resolves the application-owned `IDocumentStore` and is registered as a singleton. Handlers take it as a constructor dependency and open sessions for the ambient tenant through it:

```csharp
public sealed class PostEntryCommandHandler : ICommandHandler<PostEntryCommand>
{
    private readonly MartenTenantSessionFactory _sessions;
    private readonly IDocumentSession _session;

    public PostEntryCommandHandler(MartenTenantSessionFactory sessions, IDocumentSession session)
    {
        _sessions = sessions;
        _session = session;
    }

    public async Task HandleAsync(PostEntryCommand command, CancellationToken cancellationToken = default)
    {
        using var readSession = _sessions.OpenQuerySession();
        var duplicate = await readSession
            .Query<LedgerEntry>()
            .AnyAsync(entry => entry.EntryId == command.EntryId, cancellationToken)
            .ConfigureAwait(false);

        if (!duplicate)
        {
            _session.Store(new LedgerEntry(command.WorkspaceId, command.Draft));
        }
    }
}
```

The factory reads the ambient execution identity and opens a lightweight session for its tenant, so handlers work on tenant-filtered documents without resolving the tenant themselves. `OpenQuerySession` opens a read-only query session the same way, and the static `ResolveTenantId` returns the ambient tenant for code that needs the identifier rather than a session. Without an ambient mediation, or when the mediation runs outside any tenant, the factory opens a default session instead. See [Tenancy](../concepts/tenancy.md) for where the ambient tenant comes from.

The returned sessions sit outside the mediation unit of work: each call opens a new lightweight session the caller owns and disposes, while command handlers commit through the scoped session the unit of work saves. A handler that writes through a tenant session would commit outside the mediation transaction and split the unit of work. Handlers keep writing through the scoped session and use the factory only for tenant-filtered reads.

The application must configure Marten for conjoined tenancy, with `options.Policies.AllDocumentsAreMultiTenanted()` and no separate-database tenancy source. A source such as `options.MultiTenantedWithSingleServer(...)` keeps each tenant in its own database and disables the default tenant, so the default session the factory opens outside any tenant would fail. On a store without tenancy, opening a tenant session fails in Marten rather than filtering silently.

## Concurrency Conflicts as Refusals

`MartenConcurrencyRefusalClassifier` turns a Marten optimistic concurrency or event-stream version conflict into a denial refusal carrying `concurrency.conflict`, so a `TrySendAsync` call reports it as a value instead of raising a storage exception every application maps by hand at its boundary. It recognizes `ConcurrencyException`, which covers the event-stream version conflict `EventStreamUnexpectedMaxEventIdException` through inheritance, and `ConcurrentUpdateException`, which is how Marten surfaces a serialization conflict the application resolves by re-reading. The commit fault usually arrives wrapped, so every reachable exception is checked rather than just the top one. Lock, usage, and constraint faults pass through untouched.

The conflict is a denial rather than invalid input: the message was well-formed, but the state it was computed from is no longer current. The caller reloads and retries. An HTTP boundary answers it with `409 Conflict`. See [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md).

## Limitations

Each idempotency call runs on its own session, so the claim is not atomic with the change the handler applies. A crash between the claim commit and the handler commit leaves the key claimed for work that never happened. Only a mediation that ends inside the process settles its claim.

Keep `ClaimTimeout` longer than the longest mediation that claims through the store. A slow claimant can still lose its key to a takeover, but its late settle is then a no-op while the new claimant's work runs on. Release only deletes a claim that is still unsettled when its session commits, so a release racing a completion cannot reopen an applied key.

Success audit records share the handler transaction and roll back with it. Stage them only through the completion-stage writer, which runs before the commit. Marten must target the database the documents live in. Marten assemblies are not strong-named.

Tenant sessions filter only when Marten is configured for conjoined tenancy. Without it, opening a tenant session fails in Marten rather than filtering silently, so a host missing the tenancy policy fails its first tenant-scoped mediation instead of reading across tenants.

## Related Docs

- [In-Process Idempotency](../concepts/idempotency.md)
- [Auditing](../concepts/auditing.md)
- [Marten Audit Storage](auditing-marten.md)
- [Tenancy](../concepts/tenancy.md)
- [PostgreSQL Idempotency Store](commands-postgresql-idempotency-store.md)
- [Outbox Marten Storage](outbox-marten-storage.md)
