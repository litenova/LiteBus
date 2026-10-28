# Saga State Type Registration

- **ID**: `saga.state-registration`
- **Name**: Saga state type registration
- **Maturity**: Extension
- **Summary**: The builder maps saga definition identifiers and message contract names to CLR state types at module configuration time.

## What It Does

`SagaModuleBuilder` collects saga configuration inside the `EnableSaga()` callback. `DefineState<TState>(sagaDefinitionId)` registers a state POCO for a stable workflow partition. `MapContract(contractName, sagaDefinitionId)` links inbox message contract names to that definition so multiple commands can share one saga row. `MapState<TState>(contractName)` is a shortcut for single-contract workflows where the definition id equals the contract name.

The collected registry implements `ISagaStateTypeRegistry` and resolves definition ids from contract names at runtime in `SagaProcessorHook`.

Applications own state POCOs and completion rules. LiteBus does not generate state types.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaModuleBuilder.DefineState<TState>(...)` | `LiteBus.Saga` | Register a stable saga definition id with its CLR state type |
| `SagaModuleBuilder.RegisterState<TState>(...)` | `LiteBus.Saga` | Alias of `DefineState` for one stable saga definition id |
| `SagaModuleBuilder.MapContract(...)` | `LiteBus.Saga` | Map one contract name to the definition id |
| `SagaModuleBuilder.MapState<TState>(...)` | `LiteBus.Saga` | Shortcut for a single-contract workflow |
| `ISagaStateTypeRegistry.RegisterStateType<TState>(...)` | `LiteBus.Saga.Abstractions` | Runtime definition-to-state registration API |
| `ISagaStateTypeRegistry.ResolveDefinitionId(...)` | `LiteBus.Saga.Abstractions` | Resolve a contract to its definition id |
| `ISagaStateTypeRegistry.ResolveStateType(...)` | `LiteBus.Saga.Abstractions` | Resolve a definition id to its CLR type |

## Packages

- `LiteBus.Saga`
- `LiteBus.Saga.Abstractions`

## Requires

- `saga.inbox-integration`, which provides the `EnableSaga` configure callback.
- The mapped contract names are registered as inbox message contracts.

## Invariants

- Contract names must match `IProcessorEnvelope.ContractName` and the inbox contract registry entries.
- Unmapped contracts dispatch without saga scope.
- When a `MapContract` definition identifier has no registered state type, module build fails with `DurableStorageConfigurationException`, naming the contract and definition.
- State types require public parameterless constructors for activation and deserialization.

## Non-Goals

- The builder does not register command handlers. The command module owns handler registration.
- The builder does not validate at compile time that every saga command is mapped (see analyzers axis for durable contract rules).
- The builder does not version the saga state schema. Application migration logic owns versioning.

## Observability

Registration has no observability. A contract mapped to a definition with no registered state type fails module build. Unmapped contracts surface at runtime as inactive saga scope (`ISagaContext.IsActive == false`) with no dedicated diagnostic.

Use the following instead:

- Write integration tests that assert saga rows exist after correlated dispatch.
- Use analyzer rules for durable contract registration (`analyzers.*` axis).

## References

- [Architecture](../../architecture/README.md)
- [inbox-integration](inbox-integration.md)
- [correlation-and-tenancy](correlation-and-tenancy.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.StateTypeRegistry_ShouldResolveProcessOrderContract`

- **Use case**: The registry resolves the definition id and state type for a mapped contract name.
- **Test kind**: Unit
- **Description**: The test builds a direct `SagaStateTypeRegistry` with `RegisterStateType<OrderSagaState>("process-order")`.
- **Behavior**: The test calls `ResolveDefinitionId` and `ResolveStateType` for `process-order`.
- **Expected outcome**: The definition id is `process-order`. The state type is `OrderSagaState`.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The `MapState<TState>` registration in the `EnableSaga` callback drives hook resolution at runtime.
- **Test kind**: Integration
- **Description**: The test composition calls `registry.MapState<OrderSagaState>("orders.saga.advance")`.
- **Behavior**: A correlated dispatch runs on the mapped contract.
- **Expected outcome**: The saga row persists with the correct definition id.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Multiple contracts can map to one saga definition when using shared workflow id.
- **Test kind**: Integration
- **Description**: Workflow test support registers order workflow contract mapping.
- **Behavior**: Two different command payloads share the same correlation and definition.
- **Expected outcome**: A single saga row advances through both steps.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: The sample composition registers the saga state mapping through `EnableSaga`.
- **Test kind**: Composition
- **Description**: The test uses the sample inbox module builder configuration.
- **Behavior**: Correlated commands run on the registered contract.
- **Expected outcome**: The state persists under the sample definition id.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `DefineState` plus `MapContract` for multiple contracts sharing one definition id | Yes | Depth tests use workflow support. No test isolates the explicit multi-contract map | Unit | Low |
| Unknown definition id returns null from `ResolveStateType` | Yes | Not tested in isolation | Unit | Low |

### Out-of-Scope Use Cases

- Validating the saga mapping at compile time (analyzers axis).
- Generating state type code automatically.
- Versioning the schema for state POCOs.
