# Handler Saga Context

- **ID**: `saga.handler-context`
- **Name**: Handler saga context
- **Maturity**: Extension
- **Summary**: Injected ambient API for command handlers to read and mutate correlated saga state during inbox dispatch.

## What It Does

`ISagaContext` is the handler-facing surface for saga. When a saga scope is active, handlers call `GetState<TState>()` to read the current snapshot, `SetState(state)` to mark dirty state for post-dispatch save, and `Complete()` to mark the workflow terminal after dispatch succeeds.

`IsActive` is false when the envelope has no correlation, the contract is unmapped, or the saga instance is already completed. Handlers should guard mutations with `IsActive`.

`SagaExecutionContext` implements `ISagaContext` as a singleton with one scope per durable message ID. The persisted saga key remains `SagaCorrelation`; the message key only isolates active dispatch snapshots in memory. The same singleton instance serves the processor hook and handlers.

Saga handlers are standard `ICommandHandler<TCommand>` implementations with optional `ISagaContext` injection.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `ISagaContext.IsActive` | `LiteBus.Saga.Abstractions` | Indicates if current dispatch has an active saga scope |
| `ISagaContext.Correlation` | `LiteBus.Saga.Abstractions` | Active `SagaCorrelation`, null when inactive |
| `ISagaContext.GetState<TState>()` | `LiteBus.Saga.Abstractions` | Reads active state snapshot, throws when inactive |
| `ISagaContext.SetState<TState>(...)` | `LiteBus.Saga.Abstractions` | Marks state dirty for `AfterDispatchAsync` persistence |
| `ISagaContext.Complete()` | `LiteBus.Saga.Abstractions` | Marks scope for completion persist path |
| `SagaExecutionContext` registration | `LiteBus.Saga` | Singleton context exposed as `ISagaContext` by `SagaModule` |

## Packages

- `LiteBus.Saga.Abstractions`
- `LiteBus.Saga`

## Requires

- `saga.processor-hook` (activates scope)
- `saga.inbox-command-scope` (re-attaches scope in nested mediation scope)
- Command module and inbox dispatch

## Invariants

- `GetState`, `SetState`, and `Complete` throw when no scope is active.
- Do not call `Complete()` and `SetState()` in the same dispatch; persist final state on one message and complete on a later correlated message.
- Handlers must be idempotent under at-least-once inbox redelivery; always read current state before mutating.
- State types must be `class` with parameterless constructor (`new()` constraint).

## Non-Goals

- Does not expose saga store directly to handlers (use `ISagaContext` only).
- Does not auto-advance workflow steps or schedule timers.
- Not available on outbox handlers or non-inbox mediator calls.

## Observability

No meters, activities, or structured logging on `ISagaContext` itself.

**What to use instead**

- Application logging inside handlers when `IsActive` is false unexpectedly.
- Inbox processor metrics for redelivery counts when handlers are not idempotent.
- Store `QueryAsync` for inspecting persisted state after dispatch.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [inbox-command-scope](inbox-command-scope.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Handler reads and mutates state through `ISagaContext` during inbox dispatch.
- **Test kind**: Unit
- **Description**: `SagaMutatingDispatcher` handler checks `IsActive`, calls `GetState`/`SetState`.
- **Behavior**: In-process inbox dispatch with active saga scope.
- **Expected outcome**: Persisted state reflects handler increment.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Handler context works with PostgreSQL-backed saga store.
- **Test kind**: Integration
- **Description**: `AdvanceOrderSagaCommandHandler` uses `ISagaContext` in full composition.
- **Behavior**: Single correlated command dispatch.
- **Expected outcome**: PostgreSQL row matches handler mutation.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Handler context accumulates state across two correlated dispatches.
- **Test kind**: Integration
- **Description**: Multi-step workflow handler mutates flags and step counter.
- **Behavior**: Two inbox messages, same correlation.
- **Expected outcome**: Final state has both workflow flags and `Step == 2`.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Handler failure leaves context mutations unpersisted; compensation handler rolls back via context.
- **Test kind**: Integration
- **Description**: Failure gate on capture; compensate command clears inventory flag.
- **Behavior**: Handler throws before hook save; later handler uses context for rollback.
- **Expected outcome**: Prior saga version restored then compensated flag set.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: Handlers remain idempotent under concurrent dispatch on same correlation.
- **Test kind**: Integration
- **Description**: Increment handler with artificial delay under parallel processors.
- **Behavior**: Each handler reads current step before incrementing.
- **Expected outcome**: Final step count 1 or 2 with matching store version.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample handlers use injected `ISagaContext` across correlated commands.
- **Test kind**: Composition
- **Description**: Sample `AdvanceOrderSagaCommandHandler`.
- **Behavior**: Two correlated accepts processed sequentially.
- **Expected outcome**: `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- `ISagaContext` on outbox handlers or direct mediator calls.
- Automatic workflow advancement or timer scheduling.
- Direct `ISagaStore` access from handlers.
