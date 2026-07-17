# Inbox Command Scope Re-Attach

- **ID**: `saga.inbox-command-scope`
- **Name**: Inbox command scope re-attach
- **Maturity**: Extension
- **Summary**: Restores active saga scope inside nested command mediation scopes during in-process inbox dispatch.

## What It Does

`SagaProcessorHook.PrepareDispatchScope` attaches saga state to the processor's dispatch flow. In-process command mediation creates another execution-context boundary, so `SagaInboxCommandScopePreHandler` implements `ICommandPreHandler` and attaches the same state before command handlers run.

The in-process inbox dispatcher writes `InboxExecutionContextKeys.MessageId` to mediation settings. The pre-handler reads that durable message ID from `AmbientExecutionContext` and calls `SagaExecutionContext.TryAttach`. It does not reconstruct the saga correlation or query the state registry.

`SagaInboxCommandScopeModule` registers the pre-handler on the message registry and requires `MessageModule`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaInboxCommandScopePreHandler.PreHandleAsync(...)` | `LiteBus.Saga.InboxIntegration` | Re-attaches active saga scope in nested command mediation scope |
| `SagaInboxCommandScopeModule` | `LiteBus.Saga.InboxIntegration` | Registers pre-handler and message-registry entry |
| `InboxModuleBuilder.EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` | Entry point that wires command-scope module |

## Packages

- `LiteBus.Saga.InboxIntegration`
- `LiteBus.Saga`

## Requires

- `saga.inbox-integration`
- `saga.processor-hook` (creates scope in `BeforeDispatchAsync`)
- In-process inbox dispatch (`inbox.dispatch.in-process` or equivalent)

## Invariants

- No-op when the mediator call is not inbox execution or the durable message ID is missing.
- Does not load state from store (hook already loaded in `BeforeDispatchAsync`).
- Parallel dispatches use separate message keys even when they share one saga correlation.

## Non-Goals

- Does not apply to AMQP or other remote dispatch paths where handlers run outside the host process.
- Does not replace `PrepareDispatchScope` on the processor hook; each method attaches state at a different execution-context boundary.

## Observability

None on the pre-handler itself.

**What to use instead**

- If handlers see `IsActive == false` during inbox dispatch, verify in-process dispatch and `EnableSaga()` registration before adding custom telemetry.
- Inbox processor success/failure counters for dispatch outcomes.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [inbox-integration](inbox-integration.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Pre-handler re-attaches scope so handler can mutate saga state during in-process inbox dispatch.
- **Test kind**: Unit
- **Description**: Full processor path with `SagaInboxCommandScopeModule` equivalent wiring via manual hook registration; handler uses `ISagaContext`.
- **Behavior**: In-process dispatch with ambient inbox execution context.
- **Expected outcome**: State persisted after handler mutation.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`; pre-handler registration validated in integration and composition tests.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Scope re-attach works in full PostgreSQL integration composition.
- **Test kind**: Integration
- **Description**: `EnableSaga()` registers pre-handler module automatically.
- **Behavior**: Handler receives active context during inbox command mediation.
- **Expected outcome**: PostgreSQL saga row updated.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition pre-handler enables handler saga mutations.
- **Test kind**: Composition
- **Description**: Sample v6 `EnableSaga()` wiring.
- **Behavior**: Two correlated commands through in-process dispatch.
- **Expected outcome**: Saga step advanced to 2.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_OutsideInboxExecution_ShouldLeaveContextInactive`

- **Use case**: Direct command mediation outside inbox execution must not attach saga state.
- **Test kind**: Unit.
- **Behavior**: `PreHandleAsync` runs without an ambient inbox execution context.
- **Expected outcome**: `ISagaContext.IsActive` remains false.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_WithoutMessageId_ShouldLeaveContextInactive`

- **Use case**: An inbox marker without a durable message identifier cannot select a saga dispatch scope.
- **Test kind**: Unit.
- **Behavior**: The ambient context contains `IsInboxExecution = true` but omits `MessageId`.
- **Expected outcome**: `ISagaContext.IsActive` remains false.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_WithInboxMessageId_ShouldAttachMatchingScope`

- **Use case**: Nested command mediation must re-attach the processor-owned scope for the same durable message.
- **Test kind**: Unit with suppressed execution-context flow.
- **Behavior**: The processor hook creates a tenant-scoped saga state, then the pre-handler runs on an isolated async flow carrying the inbox message ID.
- **Expected outcome**: The exact definition, correlation, and tenant become active in the nested flow.

### Out-of-Scope Use Cases

- Remote broker inbox dispatch scope propagation.
- Outbox command mediation scope re-attach.
- Replacing processor hook `PrepareDispatchScope` behavior.
