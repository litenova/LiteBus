# Marten Unit of Work, Idempotency, and Audit

`LiteBus.Commands.Storage.Marten` binds command mediation to the Marten session the handler already writes through. `MartenUnitOfWork` commits the scoped `IDocumentSession` when the mediation succeeds, `MartenIdempotencyStore` remembers idempotency keys as Marten documents, `MartenAuditTrail` writes audit records as Marten documents, and `MartenTenantSessionFactory` opens sessions for the tenant the current mediation runs in. Use it when command handlers stage Marten documents and the commit boundary should be the session save.

For declaring idempotency keys and how the shortcut and completion handler settle them, see [In-Process Idempotency](../concepts/idempotency.md). For what an audit record contains, see [Auditing](../concepts/auditing.md).

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
        messaging.UseAuditTrail<MartenAuditTrail>();
        messaging.UseFaultRefusalClassifier<MartenConcurrencyRefusalClassifier>();
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
        commands.UseMartenTenantSessions();
    });
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

## Audit Trail

A record for a successful mediation is staged on the scoped session and committed by the same `SaveChangesAsync` that commits the handler change, so the record and the change are atomic. The ordering is fixed by priority. The audit completion handler runs at `Observability` and the unit-of-work commit runs at `UnitOfWork`, and handlers run in ascending priority order, so the writer always stages before the commit.

A record for a denial, failure, cancellation, or invalid message cannot join that transaction, because the unit of work rolls the session back on exactly those paths. Those records are written on a separate short session that saves immediately, so a refusal or a fault is still recorded when the change it describes never commits.

## Hash-Chained Audit Trail

`MartenAuditTrailOptions` turns the trail tamper-evident. Each record in a segment gets a sequence number and a SHA-256 hash over the canonical JSON of the record plus the hash of its predecessor, so a review can prove that no record was removed, reordered, or altered. Register a configured instance with the application container:

```csharp
services.AddSingleton(new MartenAuditTrailOptions().UseHashChain());
```

Without a registered instance the trail writes plain records exactly as before, with no sequence numbers and no hash chain.

Records are chained per segment. The default segment is `{category}/{year}`, with `uncategorized` for a record that carries no category. Pass a selector to `UseHashChain` to segment differently:

```csharp
services.AddSingleton(new MartenAuditTrailOptions().UseHashChain(record => record.TenantId ?? "unscoped"));
```

A chained `MartenAuditRecord` carries `Segment`, `Sequence` starting at one, `PreviousHash`, and `Hash`. The canonical form hashed is a JSON object of string fields in key order: every scalar the document carries, including the segment, the sequence, and the predecessor hash, plus each entry of `Properties` under a `property:` prefix. Dates use the round-trip format, durations use ticks, and numbers use the invariant culture, so the same document hashes the same on every machine. Only `Hash` itself is excluded: it is what is being computed.

A `MartenAuditSegmentHead` document per segment serializes appends. The document identity is the segment name, and the head carries the sequence number and hash of the last record appended. Every chained append loads the head, assigns the next sequence number, and stores the head back in the same session as the record, under Marten optimistic concurrency. A concurrent append fails its commit and retries against the winner, so two records never share a sequence number.

A chained record is written out of band on its own serialized session even for a successful mediation, because the sequence number is assigned under the segment lock at commit time, which a staged session cannot hold. A chained success record is therefore durable on its own rather than atomic with the handler change, and a rolled-back handler still leaves its audit record behind. That is the cost of the chain: enable it where tamper evidence matters more than record-change atomicity.

### Checkpoints

A hash chain proves internal consistency, but a tamperer who rewrites a whole suffix and recomputes every hash still passes verification. Published checkpoints close that gap. Set `CheckpointPublisher` to an `ICheckpointPublisher`, and the trail publishes an `AuditCheckpoint` carrying the segment, the sequence number, and the hash every `CheckpointInterval` records:

```csharp
var options = new MartenAuditTrailOptions().UseHashChain();
options.CheckpointPublisher = new LedgerCheckpointPublisher();
options.CheckpointInterval = 50;
services.AddSingleton(options);
```

`CheckpointInterval` defaults to 100 and must be positive. A checkpoint is published when a record whose sequence number is an exact multiple of the interval commits. Where the checkpoint goes is the application's decision: a separate table, a ledger, or a compliance store. Publish synchronously, because a checkpoint the application never stored anchors nothing. A publisher that throws fails the audit write it was called from, so a lost checkpoint surfaces instead of passing silently.

`VerifyChainAsync(segment)` verifies one segment from its first record to its head and returns the first broken sequence number: a missing sequence, a record whose predecessor link does not match the hash of the record before it, or a record whose stored hash does not match its recomputed hash. It returns `null` when the chain is intact, including when the segment holds no records. Verification proves that nothing was removed, reordered, or altered since the records were written. Compare the head hash against the latest checkpoint the publisher stored for the segment to prove that no suffix was rewritten.

## Tenant Sessions

`UseMartenTenantSessions` registers `MartenTenantSessionFactory` as a child module of the command module, so it is composed inside `AddCommands` and cannot be registered without the axis whose handlers open sessions through it. The factory resolves the application-owned `IDocumentStore` and is registered as a singleton. Handlers take it as a constructor dependency and open sessions for the ambient tenant through it:

```csharp
public sealed class PostEntryCommandHandler : ICommandHandler<PostEntryCommand>
{
    private readonly MartenTenantSessionFactory _sessions;

    public PostEntryCommandHandler(MartenTenantSessionFactory sessions) => _sessions = sessions;

    public async Task HandleAsync(PostEntryCommand command, CancellationToken cancellationToken = default)
    {
        using var session = _sessions.OpenSession();
        session.Store(new LedgerEntry(command.WorkspaceId, command.Draft));
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

The factory reads the ambient execution identity and opens a lightweight session for its tenant, so handlers work on tenant-filtered documents without resolving the tenant themselves. `OpenQuerySession` opens a read-only query session the same way, and the static `ResolveTenantId` returns the ambient tenant for code that needs the identifier rather than a session. Without an ambient mediation, or when the mediation runs outside any tenant, the factory opens a default session instead. See [Tenancy](../concepts/tenancy.md) for where the ambient tenant comes from.

The application must configure Marten for conjoined tenancy, with `options.Policies.AllDocumentsAreMultiTenanted()` and a multi-tenancy source such as `options.MultiTenantedWithSingleServer(...)`. On a store without tenancy, opening a tenant session fails in Marten rather than filtering silently.

## Concurrency Conflicts as Refusals

`MartenConcurrencyRefusalClassifier` turns a Marten optimistic concurrency or event-stream version conflict into a denial refusal carrying `concurrency.conflict`, so a `TrySendAsync` call reports it as a value instead of raising a storage exception every application maps by hand at its boundary. It recognizes `ConcurrencyException`, which covers the event-stream version conflict `EventStreamUnexpectedMaxEventIdException` through inheritance, and `ConcurrentUpdateException`, which is how Marten surfaces a serialization conflict the application resolves by re-reading. The commit fault usually arrives wrapped, so every reachable exception is checked rather than just the top one. Lock, usage, and constraint faults pass through untouched.

The conflict is a denial rather than invalid input: the message was well-formed, but the state it was computed from is no longer current. The caller reloads and retries. An HTTP boundary answers it with `409 Conflict`. See [ASP.NET Core Execution Identity](aspnetcore-execution-identity.md).

## Limitations

Each idempotency call runs on its own session, so the claim is not atomic with the change the handler applies. A crash between the claim commit and the handler commit leaves the key claimed for work that never happened. Only a mediation that ends inside the process settles its claim.

Keep `ClaimTimeout` longer than the longest mediation that claims through the store. A slow claimant can still lose its key to a takeover, but its late settle is then a no-op while the new claimant's work runs on. Release only deletes a claim that is still unsettled when its session commits, so a release racing a completion cannot reopen an applied key.

Success audit records share the handler transaction and roll back with it. Stage them only through the completion-stage writer, which runs before the commit. Marten must target the database the documents live in. Marten assemblies are not strong-named.

Hash-chained success records give up that atomicity. Each chained record commits on its own serialized session, so a rolled-back handler still leaves its audit record behind, and chained appends serialize per segment through the segment head. The chain suits a compliance trail written at command volume, not a hot path that needs record-change atomicity.

Tenant sessions filter only when Marten is configured for conjoined tenancy. Without it, opening a tenant session fails in Marten rather than filtering silently, so a host missing the tenancy policy fails its first tenant-scoped mediation instead of reading across tenants.

## Related Docs

- [In-Process Idempotency](../concepts/idempotency.md)
- [Auditing](../concepts/auditing.md)
- [Tenancy](../concepts/tenancy.md)
- [PostgreSQL Idempotency Store](commands-postgresql-idempotency-store.md)
- [Outbox Marten Storage](outbox-marten-storage.md)
