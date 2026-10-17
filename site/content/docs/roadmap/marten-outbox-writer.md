# Marten Transactional Outbox Writer

This page proposes an opt-in Marten transactional outbox writer for services whose domain work already runs inside a Marten document session on PostgreSQL. It is a design proposal only: none of the Marten package, builder, or participant types named here as proposed exist yet, and current transactional behaviour is unchanged.

## Problem

An integrator runs domain persistence through Marten (a PostgreSQL document database) and needs outbox rows to commit atomically with Marten documents. LiteBus ships transactional outbox writers only for raw Npgsql (`PostgreSqlOutboxStore`, `PostgreSqlTransactionalOutboxParticipant`) and Entity Framework Core (`EfCoreOutboxStore`), so the integrator hand-wrote about 150 lines of `INSERT` against the LiteBus PostgreSQL outbox table and pinned their tests to the outbox schema version. That couples them to LiteBus internals: a schema change ships silently past copied SQL, which is exactly what `PostgreSqlOutboxModuleBuilder.EnableAmbientTransactionProvider` warns against in its remarks ("Do not hand-write inserts against the outbox table: its schema is created, versioned and validated by LiteBus, and a schema change would break every such writer silently").

Two enlistment directions exist, and only one needs a new package. When LiteBus owns the PostgreSQL transaction through `IPostgreSqlTransactionProvider`, Marten already joins it via `SessionOptions.ForTransaction`, as documented on `IPostgreSqlTransactionProvider` and on `PostgreSqlOutboxModuleBuilder.EnableAmbientTransactionProvider`; `PostgreSqlMartenTransactionalOutboxTests` covers the commit and rollback of that direction, so no new package is needed for it. The gap is the reverse: the Marten `IDocumentSession` owns the transaction, and there is no LiteBus writer that binds an outbox store to the session's underlying connection and transaction. This page covers that direction.

## Package Name and Dependency Role

Proposed package: `LiteBus.Outbox.Storage.Marten`. Proposed role: feature bridge, mirroring `LiteBus.Outbox.Storage.PostgreSql`, which the role map in [Dependency Graph](../architecture/dependency-graph.md) lists under feature bridges (`Outbox.Storage.*`).

Justification from the role rules:

- A feature bridge may depend on contract roles, core implementations, technology adapters, and feature bridges, plus the relevant adapter SDK. This package would reference `LiteBus.Outbox.Abstractions`, the `LiteBus.Storage.PostgreSql` technology adapter (for the schema version constant and the bound Npgsql store), and exactly one SDK: Marten.
- A technology adapter is the wrong shape: `LiteBus.Storage.PostgreSql` owns provider mechanics shared across axes (quoting, schema versioning, advisory locks), while axis lifecycle, row mapping, and store roles stay in the `Outbox.Storage.*` bridge. A Marten package owns outbox writer binding, so it sits with the bridges.
- The per-axis split stays intact: installing the Marten outbox writer must not imply inbox storage, dispatch, ingress, or any broker SDK, per the granular opt-in rule.
- The package must not reference Entity Framework Core, dispatch, or ingress packages, and `ArchitectureDependencyPolicyTests` must resolve it to the feature bridge role before merge.

## Public API

The Npgsql transactional writer this mirrors is composed of these existing types in `src/LiteBus.Outbox.Storage.PostgreSql` and `src/LiteBus.Storage.PostgreSql`:

- `ITransactionalOutbox` is the scoped facade. `EnqueueAsync` and `EnqueueBatchAsync` take `OutboxEnqueueItem<TEvent>` (typed, built with `OutboxEnqueueItem<TEvent>.From(message)` or `with` composition over `OutboxEnqueueMetadata.Immediate`) or the untyped `OutboxEnqueueItem` batch entry. The caller owns transaction commit; processors keep using `IOutbox` with the singleton auto-commit store.
- `ITransactionalOutboxStore` marks a writer bound to the caller's transaction. Bound instances do not commit independently.
- `PostgreSqlOutboxStore.UseExistingConnection(NpgsqlConnection, NpgsqlTransaction)` returns an `ITransactionalOutboxStore` bound to the supplied pair, after verifying the transaction belongs to the connection and the connection is open.
- `PostgreSqlOutboxStoreRegistration.CreateTransactionalStore(NpgsqlConnection, NpgsqlTransaction)` is the registration-held factory for the same binding.
- `PostgreSqlTransactionalOutboxParticipant.ResolveStore()` resolves the bound store from the ambient `IPostgreSqlTransactionProvider.TryGetCurrent` pair, or falls back to the singleton store only under `TransactionalWriteMode.AllowImmediateCommit`, otherwise throwing under `TransactionalWriteMode.RequireActiveTransaction`.
- `PostgreSqlOutboxModuleBuilder.EnableAmbientTransactionProvider(TransactionalWriteMode)` registers the scoped `ITransactionalOutbox` through the ambient provider.

The Marten equivalent (all names below are proposed and do not exist yet) keeps `ITransactionalOutbox`, `OutboxEnqueueItem<TEvent>`, and `OutboxEnqueueMetadata` unchanged and adds one seam: a Marten participant that resolves the caller's document session, takes the session's underlying `NpgsqlConnection` and `NpgsqlTransaction`, and binds through `PostgreSqlOutboxStoreRegistration.CreateTransactionalStore` (or `PostgreSqlOutboxStore.UseExistingConnection`). Registration follows the existing pattern on `OutboxModuleBuilder`, mirroring `OutboxModuleBuilderPostgreSqlExtensions.UsePostgreSqlStorage`:

```csharp
// Proposed registration shape; names are not final.
builder.AddOutbox(outbox =>
{
    outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
    outbox.UseMartenStorage(marten => marten.UseDocumentStore(documentStore));
    outbox.UseInProcessDispatch();
});
```

Enqueue then stays on the existing facade inside the session boundary:

```csharp
await transactionalOutbox.EnqueueAsync(
    OutboxEnqueueItem<OrderSubmitted>.From(integrationEvent),
    cancellationToken);
```

Per [API Design](../architecture/api-design.md), the proposal adds no scalar metadata overloads, no `*Options` threading through enqueue calls, and no `CancellationToken` inside item or metadata types. If a Marten-specific option bag is needed (session lifetime, schema and table overrides), it lives on the module builder as `*Options`, and table identity stays with `PostgreSqlOutboxStoreOptions` where reuse is possible.

## Schema Ownership

The PostgreSQL outbox schema stays owned by `LiteBus.Outbox.Storage.PostgreSql`. The Marten writer must not copy DDL or hand-write column lists. The public version anchor already exists: `PostgreSqlOutboxSchema.CurrentSchemaVersion` (currently `1`), alongside `PostgreSqlOutboxSchema.GetCreateScript`, `PostgreSqlOutboxSchema.SqlFiles`, `PostgreSqlOutboxSchema.EnsureAsync`, and `PostgreSqlOutboxSchema.ValidateAsync`. The Marten package compiles against `PostgreSqlOutboxSchema.CurrentSchemaVersion`, creates and validates schema only through those entry points, and surfaces version drift through `IOutboxDiagnosticsStore.GetSchemaInfoAsync` (which returns `StoreSchemaInfo` with expected and recorded versions) rather than private SQL. Application tests assert against the constant and `ValidateAsync`, never against copied literals, so a LiteBus schema change fails the build loudly instead of corrupting writes silently.

## Migration from a Hand-Written Writer

1. Delete the copied `INSERT` and its private column list. Bind the existing `ITransactionalOutbox` facade to the Marten session transaction through the new package instead.
2. Replace schema-pinning test literals with `PostgreSqlOutboxSchema.CurrentSchemaVersion` and a `ValidateAsync` (or `GetSchemaInfoAsync`) assertion against a test database.
3. Pick the enlistment direction explicitly per boundary: LiteBus-owned transaction with Marten joining via `SessionOptions.ForTransaction` (available today through `EnableAmbientTransactionProvider`), or Marten-owned transaction with the new writer binding to the session pair. Do not mix both in one unit of work.
4. Keep `TransactionalWriteMode.RequireActiveTransaction` (the default) in production so a missing ambient transaction fails at resolve time instead of committing the outbox row on its own connection.
5. Run the new adapter against the published `LiteBus.Storage.Testing` xUnit store conformance bases before shipping, as required for custom adapter authors.

## Open Questions

- Which Marten public API exposes the session's underlying `NpgsqlConnection` and `NpgsqlTransaction` for `UseExistingConnection`? A public-docs search did not confirm the accessor; verify against current Marten documentation before fixing the participant design. If Marten only supports joining an externally owned transaction (`SessionOptions.ForTransaction`), the Marten-owned direction may be infeasible and the design should standardise on LiteBus-owned transactions instead.
- Who owns the document session lifetime: the application unit of work (consistent with `IPostgreSqlTransactionProvider`, where the application opens the connection and transaction) or a session factory owned by the package?
- Does the package reuse `PostgreSqlOutboxStoreOptions` for schema and table names, or need its own options for Marten-specific settings? Reuse is preferred unless a Marten-only setting forces a separate bag.
- How is the package versioned against outbox schema versions: does it float with the LiteBus line and assert `CurrentSchemaVersion` at startup, or pin per release?
- Should the package also cover a Marten-owned inbox accept path (`ITransactionalInboxStore` equivalent), or stay outbox-only per the axis-split rule? The default is outbox-only until a second integrator needs inbox.
