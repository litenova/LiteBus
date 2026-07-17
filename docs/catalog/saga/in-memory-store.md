# In-Memory Saga Store

- **ID**: `saga.in-memory-store`
- **Name**: In-memory saga store
- **Maturity**: Extension
- **Summary**: Thread-safe explicit saga store for unit tests and local development.

## What It Does

`InMemorySagaStore` implements `ISagaStore` using a concurrent dictionary keyed by a typed tenant, saga definition, and correlation value. The typed key prevents delimiter collisions when identifiers contain colons. State serializes through `IMessageSerializer` to JSON in memory. Select it explicitly with `SagaModuleBuilder.UseInMemoryStorage()`.

The store enforces the same optimistic concurrency rules as PostgreSQL. A new row requires expected version 0. Updates and completion require an exact version match, and completed rows reject further writes. Completion uses `ConcurrentDictionary.TryUpdate`, so two callers cannot complete one version successfully. The injected `TimeProvider` supplies creation and update timestamps used by query ordering and retention purge.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `InMemorySagaStore` | `LiteBus.Saga` | In-process `ISagaStore` implementation |
| `LoadAsync`, `SaveAsync`, `CompleteAsync` | `LiteBus.Saga` | Correlation-keyed load/save/complete with optimistic version checks |
| `QueryAsync`, `PurgeAsync` | `LiteBus.Saga` | Operational inspection and retention operations |
| `SagaModuleBuilder.UseInMemoryStorage()` | `LiteBus.Saga` | Selects `InMemorySagaStore` for the saga composition |

## Packages

- `LiteBus.Saga`

## Requires

- `saga.store` (implements contract)
- `saga.inbox-integration` (nested composition path)

## Invariants

- Process-local only; data is lost on restart.
- Same storage key algorithm as PostgreSQL store via `SagaCorrelationKey`.
- Query returns most recently updated rows first and rejects `Take` values less than 1.
- `CompletedBefore` compares the recorded completion update time, not the current clock at query time.
- Cannot be combined with another storage selection in one saga composition.

## Non-Goals

- Not for production multi-instance deployments.
- No schema migration or backup story.

## Observability

None.

**What to use instead**

- Use PostgreSQL store for production multi-instance hosts (`saga.postgresql-storage`).
- Unit and composition tests assert store outcomes via `LoadAsync` rather than internal dictionary metrics.

## Deep Docs

- [README](README.md)
- [store](store.md)
- [postgresql-storage](postgresql-storage.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Default in-memory store persists state through hook-mediated save.
- **Test kind**: Unit
- **Description**: Explicit `InMemorySagaStore` registered in test service collection.
- **Behavior**: Hook `SaveAsync` after handler mutation.
- **Expected outcome**: Load returns `Step == 1`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaProcessorHookTests.AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException`

- **Use case**: In-memory store not written when hook rejects invalid scope flags.
- **Test kind**: Unit
- **Description**: `InMemorySagaStore` behind hook; exception before save.
- **Behavior**: Invalid dirty+complete combination.
- **Expected outcome**: Exception; no row created.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `LiteBusV6CompositionSmokeTests.AddLiteBusV6_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition explicitly selects the in-memory store for a correlated workflow.
- **Test kind**: Composition
- **Description**: `AddLiteBusV6` without PostgreSQL saga storage.
- **Behavior**: Two saves through hook.
- **Expected outcome**: `Step == 2` on load.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `InMemorySagaStoreTests`

- **Use case**: Direct store behavior matches the durable optimistic-concurrency contract.
- **Test kind**: Unit
- **Description**: Covers insert and update versions, stale and completed writes, missing and repeated completion, concurrent completion, delimiter-safe identifiers, query ordering, `Take` validation, timestamps, and `CompletedBefore` purge.
- **Behavior**: Store APIs execute against one process-local instance with a controlled clock.
- **Expected outcome**: Conflicts throw `SagaConcurrencyException`; query and purge return deterministic results.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Duplicate in-memory and PostgreSQL selection fails composition | Yes | Covered by saga composition tests | Unit | Low |

### Out-of-Scope Use Cases

- Production multi-instance durability.
- Cross-process or cross-host state sharing.
- Backup and restore semantics.
