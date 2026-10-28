# In-Memory Saga Store

- **ID**: `saga.in-memory-store`
- **Name**: In-memory saga store
- **Maturity**: Extension
- **Summary**: A thread-safe, explicitly selected saga store for unit tests and local development.

## What It Does

`InMemorySagaStore` implements `ISagaStore` with a concurrent dictionary keyed by a typed value holding the tenant, the saga definition, and the correlation. When identifiers contain colons, the typed key prevents delimiter collisions. The store serializes state to JSON in memory through `IMessageSerializer`. Select it explicitly with `SagaModuleBuilder.UseInMemoryStorage()`.

The store enforces the same optimistic concurrency rules as PostgreSQL. A new row requires expected version 0. Updates and completion require an exact version match. Completed rows reject further writes. Completion uses `ConcurrentDictionary.TryUpdate`, so only one caller completes a given version. The injected `TimeProvider` supplies the creation and update timestamps. Query ordering and retention purge use those timestamps.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `InMemorySagaStore` | `LiteBus.Saga` | The in-process `ISagaStore` implementation |
| `LoadAsync`, `SaveAsync`, `CompleteAsync` | `LiteBus.Saga` | Correlation-keyed load, save, and complete with optimistic version checks |
| `QueryAsync`, `PurgeAsync` | `LiteBus.Saga` | Operational inspection and retention operations |
| `SagaModuleBuilder.UseInMemoryStorage()` | `LiteBus.Saga` | Selects `InMemorySagaStore` for the saga composition |

## Packages

- `LiteBus.Saga`

## Requires

- `saga.store`, whose contract this store implements.
- `saga.inbox-integration`, which provides the nested composition path.

## Invariants

- The store is process-local only. Data is lost on restart.
- The store uses the same composite key shape (tenant, definition, correlation) with the same whitespace-to-empty tenant normalization as the PostgreSQL store.
- Query returns the most recently updated rows first. It rejects `Take` values less than 1.
- A purge with no predicate and no `Take` throws `ArgumentException`. A set `Take` deletes at most that many of the oldest matches.
- `CompletedBefore` compares the recorded completion update time, not the current clock at query time.
- The store cannot be combined with another storage selection in one saga composition.

## Non-Goals

- The store is not for production multi-instance deployments.
- The store has no schema migration or backup story.

## Observability

The store has no dedicated observability.

Use the following instead:

- Use the PostgreSQL store for production multi-instance hosts (`saga.postgresql-storage`).
- Unit and composition tests assert store outcomes through `LoadAsync` rather than internal dictionary metrics.

## References

- [README](README.md)
- [store](store.md)
- [postgresql-storage](postgresql-storage.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: The in-memory store persists state through a hook-mediated save.
- **Test kind**: Unit
- **Description**: The test service collection registers an explicit `InMemorySagaStore`.
- **Behavior**: The hook calls `SaveAsync` after the handler mutation.
- **Expected outcome**: A load returns `Step == 1`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException`

- **Use case**: The hook rejects invalid scope flags, so the in-memory store is not written.
- **Test kind**: Unit
- **Description**: The hook uses an `InMemorySagaStore`. The exception happens before the save.
- **Behavior**: The scope combines dirty state with completion, which is invalid.
- **Expected outcome**: The hook throws. No row is created.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition explicitly selects the in-memory store for a correlated workflow.
- **Test kind**: Composition
- **Description**: The test uses `AddLiteBusV6` without PostgreSQL saga storage.
- **Behavior**: The hook saves twice.
- **Expected outcome**: A load returns `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `InMemorySagaStoreTests`

- **Use case**: Direct store behavior matches the durable optimistic-concurrency contract.
- **Test kind**: Unit
- **Description**: The tests cover insert and update versions, stale and completed writes, missing and repeated completion, concurrent completion, delimiter-safe identifiers, query ordering, `Take` validation, timestamps, and `CompletedBefore` purge.
- **Behavior**: The store APIs execute against one process-local instance with a controlled clock.
- **Expected outcome**: Conflicts throw `SagaConcurrencyException`. Query and purge return deterministic results.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPurgeBoundTests`

- **Use case**: Purge bounds refuse unbounded deletes. Under `Take`, purge deletes the oldest first.
- **Test kind**: Unit
- **Description**: The tests cover empty-filter rejection, non-positive `Take` rejection, `Take`-only oldest-first delete, and predicate-only full delete.
- **Behavior**: The purge APIs execute against one process-local instance with a controlled clock.
- **Expected outcome**: An empty filter throws `ArgumentException`. A non-positive `Take` throws `ArgumentOutOfRangeException`. Other purges return exact deleted-row counts.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Duplicate in-memory and PostgreSQL selection fails composition | Yes | Saga composition tests cover it | Unit | Low |

### Out-of-Scope Use Cases

- Production multi-instance durability.
- Cross-process or cross-host state sharing.
- Backup and restore semantics.
