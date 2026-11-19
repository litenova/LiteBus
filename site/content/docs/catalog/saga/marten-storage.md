# Marten Saga Storage

- **ID**: `saga.marten-storage`
- **Name**: Marten Saga Storage
- **Maturity**: Extension
- **Summary**: A Marten `ISagaStore` with tenant-scoped document identity and optimistic concurrency.

## What It Does

`MartenSagaStore` persists each saga instance as a `MartenSagaDocument`. The document identity is the escaped composite of the normalized tenant identifier, the saga definition identifier, and the correlation identifier, so the tenant participates in the key exactly as it does in the relational saga stores and two tenants never share a document. The parts are also stored as scalar properties so queries filter on them without unpacking the identity. State is JSON in `StateJson`, `Version` holds the optimistic lock version, and `LastAppliedMessageId` records the most recent inbox message applied to the row so a repeated delivery can be ignored safely.

`SagaModuleBuilder.UseMarten(configure)` selects `MartenSagaModule`. The store resolves the application-owned Marten `IDocumentStore` and the messaging `TimeProvider` from the dependency registry, and is registered as a singleton `ISagaStore`. The application registers Marten itself with `AddMarten`; LiteBus does not register Marten. Every call opens its own short session from the document store and commits it before returning, so one singleton instance serves every mediation.

A new saga is inserted with `Insert`, which fails when the identity already exists, and an existing saga is loaded and updated in place with `Update`, which fails when the row was removed. The document carries `UseOptimisticConcurrency`, so a concurrent writer that commits first makes the loser fail its commit. Each Marten failure is mapped to `SagaConcurrencyException`, and a stale expected version, a missing row, or a completed row is rejected before anything is stored.

Marten creates the storage for the document on first use under its default auto-create setting. The application only names it explicitly when it wants to pin the schema, with `options.Schema.For<MartenSagaDocument>()` in its Marten configuration.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaModuleBuilder.UseMarten(...)` | `LiteBus.Saga.Storage.Marten` | The nested durable saga storage selection |
| `MartenSagaModule` | `LiteBus.Saga.Storage.Marten` | Registers the Marten store as the saga storage child |
| `MartenSagaStore` | `LiteBus.Saga.Storage.Marten` | The Marten `ISagaStore` implementation |
| `MartenSagaDocument` | `LiteBus.Saga.Storage.Marten` | The document that carries one saga instance |
| `MartenSagaStoreOptions` | `LiteBus.Saga.Storage.Marten` | Store configuration, adjusted through the `configure` action; carries no settings yet |

## Packages

- `LiteBus.Saga.Storage.Marten`
- `Marten` (registered by the application with `AddMarten` against its database)

## Requires

- `saga.inbox-integration`
- `saga.store`
- An application-owned Marten `IDocumentStore` registered in dependency injection.

## Invariants

- Optimistic saves and completions compare `Version` on load and on commit; a stale expected version, a missing row, or a completed row throws `SagaConcurrencyException`.
- Marten concurrency and existence failures (`ConcurrencyException`, `ConcurrentUpdateException`, `DocumentAlreadyExistsException`, `NonExistentDocumentException`) anywhere in the inner or aggregate chain map to `SagaConcurrencyException`.
- When the hook supplies one, save and complete operations update `LastAppliedMessageId` with the inbox message identifier. A save without an applied message id keeps the existing marker instead of erasing it.
- The tenant identifier participates in the document identity. A missing or whitespace tenant identifier is stored as an empty string.
- An omitted `TenantId` query or purge predicate includes every tenant. An explicit whitespace predicate selects unscoped rows.
- Query results order by update time descending. A purge with no predicate and no `Take` throws `ArgumentException`. A set `Take` deletes at most that many of the oldest matches.
- The saga documents are separate from inbox message rows. Saga persistence and the inbox terminal update do not share a transaction.

## Non-Goals

- LiteBus does not ship an EF Core saga adapter.
- The store does not encrypt state JSON at rest (see payload encryption on the inbox and outbox axis for message payloads).

## Observability

The Marten saga store does not emit dedicated meters.

Use the following instead:

- Monitor `MartenSagaDocument` growth through the Marten database.
- Run `QueryAsync` and `PurgeAsync` for retention and support tooling.

## References

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Cookbook and scenarios](../../getting-started/cookbook.md)

## Test Coverage

### Covered Use Cases

#### `MartenSagaStoreOperationsTests.SaveAsync_SameCorrelationAcrossTenants_ShouldKeepRowsIsolated`

- **Use case**: Two tenants can use the same saga definition and correlation identifiers without sharing state.
- **Test kind**: Integration
- **Description**: The test saves distinct state under `tenant-a` and `tenant-b`, then loads and queries both documents.
- **Behavior**: The test exercises the composite document identity and the tenant query predicate against Marten.
- **Expected outcome**: Each tenant returns its own state. An omitted tenant predicate returns both documents.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/Marten/`.

#### `MartenSagaStoreOperationsTests.QueryAsync_WithFilters_ShouldReturnNewestMatchingRows`

- **Use case**: Operational queries combine definition, correlation, tenant, completion, ordering, and take predicates.
- **Test kind**: Integration
- **Description**: The test persists four documents with deterministic timestamps across two tenants and two definitions.
- **Behavior**: Documents order by update time descending.
- **Expected outcome**: Exact filters select one completed document. Take selects the newest matching document. Zero take is rejected.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/Marten/`.

#### `MartenSagaStoreOperationsTests.PurgeAsync_WithCompletedBefore_ShouldRemoveOnlyOlderCompletedRows`

- **Use case**: Retention removes completed documents before a cutoff without deleting active or newer documents.
- **Test kind**: Integration
- **Description**: The test creates old completed documents in two tenants plus a newer completed document and an active document.
- **Behavior**: The first purge is tenant-scoped. The second omits the tenant predicate and includes every tenant.
- **Expected outcome**: Two old completed documents are removed in separate calls. The newer completed and active documents remain.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/Marten/`.

#### `MartenSagaStoreOperationsTests.CompleteAsync_ConcurrentWorkers_ShouldAllowOneCompletion`

- **Use case**: Two workers race to complete the same expected version.
- **Test kind**: Integration
- **Description**: The test starts two `CompleteAsync` operations against version 1 of one Marten document.
- **Behavior**: The loaded document is updated in place, asserting the version captured at load.
- **Expected outcome**: One call succeeds. One throws `SagaConcurrencyException`. The stored document reaches completed version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/Marten/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| End-to-end saga dispatch through the inbox hook against Marten | Yes | Only direct store operations are tested | Integration | Low |
| Purge bound validation against Marten | Yes | Covered for PostgreSQL and in-memory stores only | Integration | Low |

### Out-of-Scope Use Cases

- EF Core saga adapter.
- Migration of incompatible existing saga documents.
- Encryption of state JSON at rest.
