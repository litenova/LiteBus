# Processor Envelope Hooks

- **ID**: `saga.processor-envelope-hooks`
- **Name**: Processor envelope hooks
- **Maturity**: GA
- **Summary**: Axis-neutral hooks that run before and after durable message dispatch while the processor lease is active.

## What It Does

`IProcessorEnvelopeHook` lets features participate in inbox and outbox processor dispatch without referencing axis-specific envelope types. Hosts resolve all registered hooks and invoke them in registration order. Hooks run after a lease is acquired and before terminal persistence.

The successful hook path has three phases: `BeforeDispatchAsync`, optional synchronous `PrepareDispatchScope` (default no-op), and `AfterDispatchAsync`. `AbandonDispatchScope` releases hook-owned state when dispatch is canceled, dispatch fails, or the after-dispatch sequence stops on an exception. Hook failures transition the envelope according to processor policy; the handler is not re-run for an after-dispatch failure.

`IProcessorEnvelope` is the read-only envelope view hooks receive: message id, contract name and version, correlation id, causation id, and tenant id.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `IProcessorEnvelopeHook.BeforeDispatchAsync(...)` | `LiteBus.Orchestration.Abstractions` | Runs after lease and before dispatcher call |
| `IProcessorEnvelopeHook.PrepareDispatchScope(...)` | `LiteBus.Orchestration.Abstractions` | Optional synchronous ambient-scope phase before dispatch |
| `IProcessorEnvelopeHook.AfterDispatchAsync(...)` | `LiteBus.Orchestration.Abstractions` | Runs after dispatch and before terminal persistence |
| `IProcessorEnvelopeHook.AbandonDispatchScope(...)` | `LiteBus.Orchestration.Abstractions` | Releases state after canceled or failed processing |
| `IProcessorEnvelope` | `LiteBus.Orchestration.Abstractions` | Read-only hook envelope metadata |
| Hook registration through `DependencyRegistry` | `LiteBus.Runtime.Abstractions` | Invocation order follows DI registration order |

## Packages

- `LiteBus.Orchestration.Abstractions`

## Requires

- `runtime.module-configuration` (module registration)
- Inbox or outbox processor with hook invocation (outside this axis)

## Invariants

- Hooks run while the active lease is held.
- Multiple hooks run in DI registration order.
- Hook failure dead-letters the envelope; handler dispatch is not retried for hook errors.
- `PrepareDispatchScope` is synchronous so ambient values are assigned on the logical flow that immediately invokes the dispatcher.
- `AbandonDispatchScope` must release in-memory scope without performing durable side effects.

## Non-Goals

- Does not define saga-specific behavior (see `saga.processor-hook`).
- Does not run on direct in-process mediator calls outside durable processors.
- Outbox saga integration is not shipped in v6.

## Observability

No dedicated meters or activity sources on `IProcessorEnvelopeHook` itself.

**What to use instead**

- Inbox processor counters (`litebus.inbox.processor.succeeded`, `failed`, `dead_lettered`) reflect envelopes that passed through hooks.
- Application logging inside custom hook implementations.
- Saga persistence outcomes surface indirectly through hook exceptions and inbox terminal state (see `saga.processor-hook`).

## Deep Docs

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [inbox-integration](inbox-integration.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: When a saga hook runs on inbox dispatch, before and after phases load and persist state around handler execution.
- **Test kind**: Unit
- **Description**: `PipelinedInboxProcessor` with `SagaProcessorHook` registered; in-memory inbox and saga stores.
- **Behavior**: Correlated `process-order` command accepted and processed.
- **Expected outcome**: Saga row exists with `Step == 1` after one processor pass.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`; exercises all three hook phases indirectly.

#### `SagaProcessorHookTests.AfterDispatch_WhenDirtyAndComplete_ThrowsInvalidOperationException`

- **Use case**: When `AfterDispatchAsync` detects dirty state and completion in the same dispatch, persistence is rejected.
- **Test kind**: Unit
- **Description**: Direct `SagaProcessorHook.AfterDispatchAsync` after `SetState` and `Complete` on active scope.
- **Behavior**: Hook after-dispatch persist path.
- **Expected outcome**: `InvalidOperationException` before store write.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Hook phases integrate with PostgreSQL saga storage and real inbox processor.
- **Test kind**: Integration
- **Description**: Full `AddLiteBus` composition with `EnableSaga()` and `UsePostgreSqlSagaStorage`.
- **Behavior**: Single correlated inbox command processed.
- **Expected outcome**: Saga instance loaded from PostgreSQL with expected state.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Multiple inbox messages for one correlation invoke hook load/save across two dispatches.
- **Test kind**: Integration
- **Description**: Two-step order workflow with reserve and capture commands.
- **Behavior**: Two processor passes on correlated accepts.
- **Expected outcome**: Single saga row at version 2 with both flags set.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state`

- **Use case**: Handler failure skips after-dispatch save; a later correlated message restores state through hook reload.
- **Test kind**: Integration
- **Description**: Simulated capture failure then compensation command.
- **Behavior**: Failed dispatch leaves saga at prior version; compensation dispatch persists rollback.
- **Expected outcome**: Saga version and flags reflect compensation without phantom partial capture state.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment`

- **Use case**: Concurrent processor workers invoke hook concurrency retry paths under optimistic locking.
- **Test kind**: Integration
- **Description**: Four parallel `PipelinedInboxProcessor` instances racing on two correlated increment commands.
- **Behavior**: Hook after-dispatch with PostgreSQL optimistic version checks.
- **Expected outcome**: At most one version increment lost; single saga row; inbox terminals consistent with final step count.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddLiteBusV6_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample v6 composition registers saga hook and persists state across correlated commands.
- **Test kind**: Composition
- **Description**: `AddV6CompositionSmoke` with in-memory stores from composition smoke tests.
- **Behavior**: Two correlated accepts and two processor passes.
- **Expected outcome**: Saga state `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Hook failure in `BeforeDispatchAsync` dead-letters envelope without handler run | Yes | Inbox processor tests cover generic hooks; saga-specific hook not isolated | Unit | Low |
| Multiple custom hooks invoked in DI registration order | Yes | Only saga hook tested in saga axis | Unit | Low |
| `PrepareDispatchScope` and `AbandonDispatchScope` default no-op behavior | Yes | Implicit through existing non-saga hooks | Unit | Low |

### Out-of-Scope Use Cases

- Outbox processor envelope hooks with saga state (not shipped in v6).
- Hook invocation on direct in-process mediator calls outside durable processors.
- Dedicated OpenTelemetry meters on hook contract.
