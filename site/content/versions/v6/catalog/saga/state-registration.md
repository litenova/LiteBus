# Saga State Type Registration

- **ID**: `saga.state-registration`
- **Name**: Saga state type registration
- **Maturity**: Extension
- **Summary**: Maps saga definition identifiers and message contract names to CLR state types at module configuration time.

## What It Does

`SagaModuleBuilder` collects saga configuration inside the `EnableSaga()` callback. `DefineState<TState>(sagaDefinitionId)` registers a state POCO for a stable workflow partition. `MapContract(contractName, sagaDefinitionId)` links inbox message contract names to that definition so multiple commands can share one saga row. `MapState<TState>(contractName)` is sugar for single-contract workflows where definition id equals contract name.

The collected registry implements `ISagaStateTypeRegistry` and resolves definition ids from contract names at runtime in `SagaProcessorHook`.

Applications own state POCOs and completion rules; LiteBus does not generate state types.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaModuleBuilder.DefineState<TState>(...)` | `LiteBus.Saga` | Register stable saga definition id to CLR state type |
| `SagaModuleBuilder.MapContract(...)` | `LiteBus.Saga` | Map one contract name to definition id |
| `SagaModuleBuilder.MapState<TState>(...)` | `LiteBus.Saga` | Shortcut for single-contract workflow |
| `ISagaStateTypeRegistry.RegisterStateType<TState>(...)` | `LiteBus.Saga.Abstractions` | Runtime definition-to-state registration API |
| `ISagaStateTypeRegistry.ResolveDefinitionId(...)` | `LiteBus.Saga.Abstractions` | Resolve contract to definition id |
| `ISagaStateTypeRegistry.ResolveStateType(...)` | `LiteBus.Saga.Abstractions` | Resolve definition id to CLR type |

## Packages

- `LiteBus.Saga`
- `LiteBus.Saga.Abstractions`

## Requires

- `saga.inbox-integration` (`EnableSaga` configure callback)
- Inbox message contract registration for mapped contract names

## Invariants

- Contract names must match `IProcessorEnvelope.ContractName` / inbox contract registry entries.
- Unmapped contracts dispatch without saga scope.
- State types require public parameterless constructors for activation and deserialization.

## Non-Goals

- Does not register command handlers (command module owns handler registration).
- Does not validate at compile time that every saga command is mapped (see analyzers axis for durable contract rules).
- Does not version saga state schema (application migration logic).

## Observability

None at registration time. Misconfigured mappings surface at runtime as inactive saga scope (`ISagaContext.IsActive == false`) with no dedicated diagnostic.

**What to use instead**

- Integration tests asserting saga rows exist after correlated dispatch.
- Analyzer rules for durable contract registration (`analyzers.*` axis).

## Deep Docs

- [Architecture](../../architecture/README.md)
- [inbox-integration](inbox-integration.md)
- [correlation-and-tenancy](correlation-and-tenancy.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.StateTypeRegistry_ShouldResolveProcessOrderContract`

- **Use case**: Registry resolves definition id and state type for a mapped contract name.
- **Test kind**: Unit
- **Description**: Direct `SagaStateTypeRegistry` with `RegisterStateType<OrderSagaState>("process-order")`.
- **Behavior**: `ResolveDefinitionId` and `ResolveStateType` for `process-order`.
- **Expected outcome**: Definition id `process-order`; state type `OrderSagaState`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: `MapState<TState>` registration in `EnableSaga` callback drives hook resolution at runtime.
- **Test kind**: Integration
- **Description**: `registry.MapState<OrderSagaState>("orders.saga.advance")` in test composition.
- **Behavior**: Correlated dispatch on mapped contract.
- **Expected outcome**: Saga row persisted with correct definition id.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Multiple contracts can map to one saga definition when using shared workflow id.
- **Test kind**: Integration
- **Description**: Workflow test support registers order workflow contract mapping.
- **Behavior**: Two different command payloads, same correlation and definition.
- **Expected outcome**: Single saga row advanced through both steps.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition registers saga state mapping via `EnableSaga`.
- **Test kind**: Composition
- **Description**: Sample inbox module builder configuration.
- **Behavior**: Correlated commands on registered contract.
- **Expected outcome**: State persisted under sample definition id.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `DefineState` plus `MapContract` for multiple contracts sharing one definition id | Yes | Depth tests use workflow support; explicit multi-contract map not isolated | Unit | Low |
| Unknown definition id returns null from `ResolveStateType` | Yes | Not tested in isolation | Unit | Low |

### Out-of-Scope Use Cases

- Compile-time saga mapping validation (analyzers axis).
- Automatic state type code generation.
- Schema versioning for state POCOs.
