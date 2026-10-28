# Handler Saga Context

- **ID**: `saga.handler-context`
- **Name**: Handler saga context
- **Maturity**: Extension
- **Summary**: An injected ambient API lets command handlers read and mutate correlated saga state during inbox dispatch.

## What It Does

`ISagaContext` is the handler-facing surface for saga. When a saga scope is active, handlers call `GetState<TState>()` to read the current snapshot. They call `SetState(state)` to replace the state for the post-dispatch save. They call `Complete()` to mark the workflow terminal after dispatch succeeds.

Reading state never marks the scope as changed. In-place mutations of the returned instance persist without `SetState`, because the hook compares the serialized state with the snapshot taken at load. A dispatch that only reads state saves nothing.

When the envelope has no correlation, the contract is unmapped, the saga instance is already completed, or the message was already applied (redelivery marker match), `IsActive` is false. The handler still runs for a new message on a completed saga. A false `IsActive` only disables saga state access, so handlers should guard mutations with `IsActive`.

`SagaExecutionContext` implements `ISagaContext` as a singleton with one scope per durable message id. The persisted saga key remains `SagaCorrelation`. The message key only isolates active dispatch snapshots in memory. The same singleton instance serves the processor hook and handlers.

Saga handlers are standard `ICommandHandler<TCommand>` implementations that optionally inject `ISagaContext`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `ISagaContext.IsActive` | `LiteBus.Saga.Abstractions` | Tells whether the current dispatch has an active saga scope |
| `ISagaContext.Correlation` | `LiteBus.Saga.Abstractions` | The active `SagaCorrelation`, null when inactive |
| `ISagaContext.GetState<TState>()` | `LiteBus.Saga.Abstractions` | Reads the active state snapshot and throws when inactive |
| `ISagaContext.SetState<TState>(...)` | `LiteBus.Saga.Abstractions` | Marks state dirty for persistence in `AfterDispatchAsync` |
| `ISagaContext.Complete()` | `LiteBus.Saga.Abstractions` | Marks the scope for the completion persist path |
| `SagaExecutionContext` registration | `LiteBus.Saga` | Singleton context exposed as `ISagaContext`, registered by `SagaModule` |

## Packages

- `LiteBus.Saga.Abstractions`
- `LiteBus.Saga`

## Requires

- `saga.processor-hook`, which activates the scope.
- `saga.inbox-command-scope`, which re-attaches the scope in the nested mediation scope.
- The command module and inbox dispatch.

## Invariants

- When no scope is active, `GetState`, `SetState`, and `Complete` throw.
- Do not combine `Complete()` with state changes in the same dispatch, either through `SetState()` or through in-place mutation of the `GetState()` instance. Persist final state on one message and complete on a later correlated message.
- Handlers must be idempotent under at-least-once inbox redelivery. Always read current state before mutating.
- State types must be a `class` with a parameterless constructor (the `new()` constraint).

## Non-Goals

- The saga does not expose the store directly to handlers. Use `ISagaContext` only.
- The saga does not auto-advance workflow steps or schedule timers.
- The context is not available on outbox handlers or non-inbox mediator calls.

## Observability

`ISagaContext` itself has no meters, activities, or structured logging.

Use the following instead:

- Add application logging inside handlers when `IsActive` is false unexpectedly.
- Use inbox processor metrics for redelivery counts when handlers are not idempotent.
- Use the store `QueryAsync` to inspect persisted state after dispatch.

## References

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [inbox-command-scope](inbox-command-scope.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: The handler reads and mutates state through `ISagaContext` during inbox dispatch.
- **Test kind**: Unit
- **Description**: The `SagaMutatingDispatcher` handler checks `IsActive` and calls `GetState` and `SetState`.
- **Behavior**: Inbox dispatch runs in-process with an active saga scope.
- **Expected outcome**: The persisted state reflects the handler increment.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenDispatchOnlyReadsState_SkipsSave`

- **Use case**: A handler that observes state through `GetState` without changing it performs no save.
- **Test kind**: Unit
- **Description**: The handler reads the snapshot and returns it unchanged.
- **Behavior**: After dispatch, the serialized state matches the load snapshot.
- **Expected outcome**: The hook saves nothing and creates no row for the new saga.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `SagaPureReadTests.AfterDispatchAsync_WhenStateChangesInPlace_PersistsWithoutSetState`

- **Use case**: When the handler mutates the `GetState` instance in place, the change persists without `SetState`.
- **Test kind**: Unit
- **Description**: The handler sets a property on the returned state object.
- **Behavior**: The serialized state differs from the load snapshot, so the hook saves.
- **Expected outcome**: The stored row carries the mutation at version 1.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The handler context persists state through the PostgreSQL-backed saga store.
- **Test kind**: Integration
- **Description**: `AdvanceOrderSagaCommandHandler` uses `ISagaContext` in full composition.
- **Behavior**: The test dispatches a single correlated command.
- **Expected outcome**: The PostgreSQL row matches the handler mutation.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: The handler context accumulates state across two correlated dispatches.
- **Test kind**: Integration
- **Description**: The multi-step workflow handler mutates flags and the step counter.
- **Behavior**: Two inbox messages share the same correlation.
- **Expected outcome**: The final state has both workflow flags and `Step == 2`.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: A handler failure leaves context mutations unpersisted. The compensation handler rolls back through the context.
- **Test kind**: Integration
- **Description**: A failure gate blocks capture. The compensate command clears the inventory flag.
- **Behavior**: The handler throws before the hook saves. A later handler uses the context for rollback.
- **Expected outcome**: The prior saga version is restored, then the compensated flag is set.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: Handlers remain idempotent under concurrent dispatch on the same correlation.
- **Test kind**: Integration
- **Description**: The increment handler runs with an artificial delay under parallel processors.
- **Behavior**: Each handler reads the current step before incrementing.
- **Expected outcome**: The final step count is 1 or 2 with a matching store version.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample handlers use injected `ISagaContext` across correlated commands.
- **Test kind**: Composition
- **Description**: The test uses the sample `AdvanceOrderSagaCommandHandler`.
- **Behavior**: The test processes two correlated accepts sequentially.
- **Expected outcome**: The state reaches `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Using `ISagaContext` on outbox handlers or direct mediator calls.
- Advancing workflows automatically or scheduling timers.
- Handlers accessing `ISagaStore` directly.
