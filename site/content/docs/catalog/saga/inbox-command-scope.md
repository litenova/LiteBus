# Inbox Command Scope Re-Attach

- **ID**: `saga.inbox-command-scope`
- **Name**: Inbox command scope re-attach
- **Maturity**: Extension
- **Summary**: The pre-handler restores the active saga scope inside nested command mediation scopes during in-process inbox dispatch.

## What It Does

`SagaProcessorHook.PrepareDispatchScope` attaches saga state to the processor's dispatch flow. In-process command mediation creates another execution-context boundary. `SagaInboxCommandScopePreHandler` therefore implements `ICommandPreHandler` and attaches the same state before command handlers run.

The in-process inbox dispatcher writes `InboxExecutionContextKeys.MessageId` to mediation settings. The pre-handler reads that durable message id from `AmbientExecutionContext` and calls `SagaExecutionContext.TryAttach`. It does not reconstruct the saga correlation or query the state registry.

`SagaInboxCommandScopeModule` registers the pre-handler on the message registry and requires `MessageModule`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaInboxCommandScopePreHandler.PreHandleAsync(...)` | `LiteBus.Saga.InboxIntegration` | Re-attaches the active saga scope in the nested command mediation scope |
| `SagaInboxCommandScopeModule` | `LiteBus.Saga.InboxIntegration` | Registers the pre-handler and the message-registry entry |
| `InboxModuleBuilder.EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` | Entry point that wires the command-scope module |

## Packages

- `LiteBus.Saga.InboxIntegration`
- `LiteBus.Saga`

## Requires

- `saga.inbox-integration`
- `saga.processor-hook`, which creates the scope in `BeforeDispatchAsync`.
- The dispatch runs through in-process inbox dispatch (`inbox.dispatch.in-process` or equivalent).

## Invariants

- When the mediator call is not inbox execution or the durable message id is missing, the pre-handler does nothing.
- The pre-handler does not load state from the store. The hook already loaded it in `BeforeDispatchAsync`.
- Parallel dispatches use separate message keys even when they share one saga correlation.

## Non-Goals

- The pre-handler does not apply to AMQP or other remote dispatch paths where handlers run outside the host process.
- The pre-handler does not replace `PrepareDispatchScope` on the processor hook. Each method attaches state at a different execution-context boundary.

## Observability

The pre-handler itself has no observability.

Use the following instead:

- If handlers see `IsActive == false` during inbox dispatch, verify in-process dispatch and `EnableSaga()` registration before adding custom telemetry.
- Use inbox processor success and failure counters for dispatch outcomes.

## References

- [Architecture](../../architecture/README.md)
- [processor-hook](processor-hook.md)
- [inbox-integration](inbox-integration.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: The pre-handler re-attaches the scope so the handler can mutate saga state during in-process inbox dispatch.
- **Test kind**: Unit
- **Description**: The test runs the full processor path with `SagaInboxCommandScopeModule` equivalent wiring through manual hook registration. The handler uses `ISagaContext`.
- **Behavior**: Dispatch runs in-process with the ambient inbox execution context.
- **Expected outcome**: The state persists after the handler mutation.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`. Pre-handler registration is validated in integration and composition tests.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The pre-handler re-attaches the scope in the full PostgreSQL integration composition.
- **Test kind**: Integration
- **Description**: `EnableSaga()` registers the pre-handler module automatically.
- **Behavior**: The handler receives the active context during inbox command mediation.
- **Expected outcome**: The PostgreSQL saga row is updated.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: In the sample composition, the pre-handler lets handlers mutate saga state.
- **Test kind**: Composition
- **Description**: The test uses the sample `EnableSaga()` wiring.
- **Behavior**: Two correlated commands run through in-process dispatch.
- **Expected outcome**: The saga step advances to 2.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_OutsideInboxExecution_ShouldLeaveContextInactive`

- **Use case**: Direct command mediation outside inbox execution must not attach saga state.
- **Test kind**: Unit
- **Behavior**: `PreHandleAsync` runs without an ambient inbox execution context.
- **Expected outcome**: `ISagaContext.IsActive` remains false.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_WithoutMessageId_ShouldLeaveContextInactive`

- **Use case**: An inbox marker without a durable message identifier cannot select a saga dispatch scope.
- **Test kind**: Unit
- **Behavior**: The ambient context contains `IsInboxExecution = true` but omits `MessageId`.
- **Expected outcome**: `ISagaContext.IsActive` remains false.

#### `SagaInboxCommandScopePreHandlerTests.PreHandleAsync_WithInboxMessageId_ShouldAttachMatchingScope`

- **Use case**: Nested command mediation must re-attach the processor-owned scope for the same durable message.
- **Test kind**: Unit with suppressed execution-context flow
- **Behavior**: The processor hook creates a tenant-scoped saga state. The pre-handler then runs on an isolated async flow carrying the inbox message id.
- **Expected outcome**: The exact definition, correlation, and tenant become active in the nested flow.

### Out-of-Scope Use Cases

- Propagating the scope through remote broker inbox dispatch.
- Re-attaching the scope for outbox command mediation.
- Replacing the processor hook `PrepareDispatchScope` behavior.
