# Inbox Saga Integration

- **ID**: `saga.inbox-integration`
- **Name**: Inbox saga integration
- **Maturity**: Extension
- **Summary**: The integration registers saga modules, the processor hook, explicit storage, and command scope support on the inbox module builder.

## What It Does

`InboxModuleBuilder.EnableSaga(configure)` is the single entry point for inbox-hosted saga. It registers `SagaModule` (hook, execution context, state registry, and selected store) and `SagaInboxCommandScopeModule` (command pre-handler for scope re-attach).

Saga integrates as a child module of inbox configuration, which preserves dependency role rules. Saga core does not reference inbox abstractions on public surfaces. The integration package connects the inbox and saga builders.

Composition uses `AddInbox(inbox => inbox.EnableSaga(...))`. There is no top-level `AddSagaModule(...)` API.

The callback must select exactly one store. Use `UseInMemoryStorage()`, `UsePostgreSqlStorage(...)` (see `saga.postgresql-storage`), or `SagaModuleBuilder.RegisterStorage(...)` for a custom `ISagaStorageModule` (see `saga.store`).

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `InboxModuleBuilder.EnableSaga(Action<SagaModuleBuilder>)` | `LiteBus.Saga.InboxIntegration` | Main saga registration entry point on the inbox builder |
| `SagaModuleBuilder.UseInMemoryStorage()` | `LiteBus.Saga` | Explicit process-local store selection |
| `SagaModuleBuilder.UsePostgreSqlStorage(...)` | `LiteBus.Saga.Storage.PostgreSql` | Explicit durable store selection |
| `SagaModule` | `LiteBus.Saga` | Registers the hook, context, registry, and selected store |
| `SagaInboxCommandScopeModule` | `LiteBus.Saga.InboxIntegration` | Registers the command pre-handler for scope re-attach |

## Packages

- `LiteBus.Saga.InboxIntegration`
- `LiteBus.Saga` (transitive through module registration)
- `LiteBus.Saga.Storage.PostgreSql` (optional)

## Requires

- The inbox module with the processor enabled (`inbox.*` axis).
- The command module for handler dispatch.
- `saga.state-registration`, which the configure callback sets up.

## Invariants

- `EnableSaga()` must be called on the inbox module builder. There is no top-level registry shortcut.
- When storage is missing or selected twice, composition fails.
- Inbox processor options such as `BatchSize`, `LeaseDuration`, and `DispatcherConcurrency` apply unchanged.

## Non-Goals

- The integration does not enable saga on outbox processors.
- The integration does not register inbox storage or dispatch. The application composes those separately.
- The integration does not wire saga to ingress transports directly. The correlation must be on accepted inbox items.

## Observability

The integration uses inbox processor observability. There is no separate manifest entry or meter for `EnableSaga()`.

Use the following instead:

- `LiteBusHostManifest` lists the inbox processor background service, which saga leaves unchanged.
- Use inbox processor counters for dispatch outcomes.
- Use composition smoke tests to verify that `ISagaStore` and `ISagaContext` resolve from DI.

## References

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [README](README.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: `EnableSaga` with PostgreSQL storage persists state after inbox dispatch.
- **Test kind**: Integration
- **Description**: The test builds a full `AddLiteBus` with inbox PostgreSQL storage, in-process dispatch, `EnableSaga`, and `UsePostgreSqlStorage`.
- **Behavior**: The test runs a correlated accept and a processor pass.
- **Expected outcome**: The saga instance loads from the PostgreSQL store.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: `EnableSaga` wiring supports multi-step workflows with durable storage.
- **Test kind**: Integration
- **Description**: The test builds a shared saga test provider through the `EnableSaga` callback.
- **Behavior**: Two correlated accepts run through two processor passes.
- **Expected outcome**: A single saga row remains at version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Integration composition survives handler failure and compensation paths.
- **Test kind**: Integration
- **Description**: The test uses the same `EnableSaga` provider as the depth tests.
- **Behavior**: The failure commands run first. The compensation commands run after.
- **Expected outcome**: The saga state is rolled back and compensated.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: `EnableSaga` registers the hook and the store for concurrent processor scenarios.
- **Test kind**: Integration
- **Description**: Parallel processors run against the shared `EnableSaga` composition.
- **Behavior**: Increment commands race on one correlation.
- **Expected outcome**: The version increment stays bounded. A single saga row remains.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldRegisterCoreServicesAndHostedProcessors`

- **Use case**: The sample composition registers saga services alongside inbox and outbox processors.
- **Test kind**: Composition
- **Description**: `AddLiteBusV6` resolves `ISagaStore` and `ISagaContext`.
- **Behavior**: Services resolve from DI after the module build.
- **Expected outcome**: The saga services are non-null. The manifest lists processor background services.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample `EnableSaga` configuration persists in-memory saga state.
- **Test kind**: Composition
- **Description**: The sample host runs with an explicitly selected in-memory saga store.
- **Behavior**: The test processes two correlated commands.
- **Expected outcome**: The state reaches `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `EnableSaga(...)` with `UseInMemoryStorage()` registers the process-local store | Yes | The composition uses sample wiring | Unit | Low |
| Calling `EnableSaga()` twice throws or replaces registration | Yes | Not tested | Unit | Low |
| `EnableSaga(...)` without a store fails composition | Yes | Not tested | Unit | Low |

### Out-of-Scope Use Cases

- Calling `EnableSaga()` on the outbox module.
- Wiring ingress transports without an inbox accept.
- Integrating saga with the outbox.
