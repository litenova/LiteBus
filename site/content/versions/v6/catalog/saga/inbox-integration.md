# Inbox Saga Integration

- **ID**: `saga.inbox-integration`
- **Name**: Inbox saga integration
- **Maturity**: Extension
- **Summary**: Registers saga modules, processor hook, explicit storage, and command scope support on the inbox module builder.

## What It Does

`InboxModuleBuilder.EnableSaga(configure)` is the single entry point for inbox-hosted saga. It registers `SagaModule` (hook, execution context, state registry, and selected store) and `SagaInboxCommandScopeModule` (command pre-handler for scope re-attach).

Saga integrates as a child module of inbox configuration, preserving dependency role rules: saga core does not reference inbox abstractions on public surfaces; the integration package bridges builders.

Composition uses `AddInbox(inbox => inbox.EnableSaga(...))`. There is no top-level `AddSagaModule(...)` API.

The callback must select exactly one store through `UseInMemoryStorage()` or `UsePostgreSqlStorage(...)` (see `saga.postgresql-storage`).

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `InboxModuleBuilder.EnableSaga(Action<SagaModuleBuilder>?)` | `LiteBus.Saga.InboxIntegration` | Main saga registration entry point on inbox builder |
| `SagaModuleBuilder.UseInMemoryStorage()` | `LiteBus.Saga` | Explicit process-local store selection |
| `SagaModuleBuilder.UsePostgreSqlStorage(...)` | `LiteBus.Saga.Storage.PostgreSql` | Explicit durable store selection |
| `SagaModule` | `LiteBus.Saga` | Registers hook, context, registry, and selected store |
| `SagaInboxCommandScopeModule` | `LiteBus.Saga.InboxIntegration` | Registers command pre-handler for scope re-attach |

## Packages

- `LiteBus.Saga.InboxIntegration`
- `LiteBus.Saga` (transitive via module registration)
- `LiteBus.Saga.Storage.PostgreSql` (optional)

## Requires

- Inbox module with processor enabled (`inbox.*` axis)
- Command module for handler dispatch
- `saga.state-registration` (configure callback)

## Invariants

- `EnableSaga()` must be called on the inbox module builder; there is no top-level registry shortcut.
- Missing storage and duplicate storage selections fail composition.
- Inbox processor options (`BatchSize`, `LeaseDuration`, `DispatcherConcurrency`, and similar) apply unchanged.

## Non-Goals

- Does not enable saga on outbox processors.
- Does not register inbox storage or dispatch (application composes those separately).
- Does not wire saga to ingress transports directly (correlation must be on accepted inbox items).

## Observability

Uses inbox processor observability; no separate manifest entry or meter for `EnableSaga()`.

**What to use instead**

- `LiteBusHostManifest` lists inbox processor background service (unchanged by saga).
- Inbox processor counters for dispatch outcomes.
- Composition smoke tests to verify `ISagaStore` and `ISagaContext` resolve from DI.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [README](README.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: `EnableSaga` with PostgreSQL storage persists state after inbox dispatch.
- **Test kind**: Integration
- **Description**: Full `AddLiteBus` with inbox PostgreSQL storage, in-process dispatch, `EnableSaga`, and `UsePostgreSqlStorage`.
- **Behavior**: Correlated accept and processor pass.
- **Expected outcome**: Saga instance loaded from PostgreSQL store.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: `EnableSaga` wiring supports multi-step workflows with durable storage.
- **Test kind**: Integration
- **Description**: Shared saga test provider built via `EnableSaga` callback.
- **Behavior**: Two correlated accepts, two processor passes.
- **Expected outcome**: Single saga row at version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Integration composition survives handler failure and compensation paths.
- **Test kind**: Integration
- **Description**: Same `EnableSaga` provider as depth tests.
- **Behavior**: Failure then compensation correlated commands.
- **Expected outcome**: Saga state rolled back and compensated.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: `EnableSaga` registers hook and store for concurrent processor scenarios.
- **Test kind**: Integration
- **Description**: Parallel processors against shared `EnableSaga` composition.
- **Behavior**: Racing increment commands on one correlation.
- **Expected outcome**: Bounded version increment; single saga row.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldRegisterCoreServicesAndHostedProcessors`

- **Use case**: Sample composition registers saga services alongside inbox/outbox processors.
- **Test kind**: Composition
- **Description**: `AddLiteBusV6` resolves `ISagaStore` and `ISagaContext`.
- **Behavior**: DI service resolution after module build.
- **Expected outcome**: Saga services non-null; manifest lists processor background services.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample `EnableSaga` configuration persists in-memory saga state.
- **Test kind**: Composition
- **Description**: Sample host with an explicitly selected in-memory saga store.
- **Behavior**: Two correlated commands processed.
- **Expected outcome**: `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `EnableSaga(...)` with `UseInMemoryStorage()` registers the process-local store | Yes | Composition uses sample wiring | Unit | Low |
| Calling `EnableSaga()` twice throws or replaces registration | Yes | Not tested | Unit | Low |
| `EnableSaga(...)` without a store fails composition | Yes | Covered by saga composition tests | Unit | Low |

### Out-of-Scope Use Cases

- `EnableSaga()` on outbox module.
- Ingress transport wiring without inbox accept.
- Outbox saga integration.
