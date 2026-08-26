# Execution Context

- **ID**: `mediator.execution-context`
- **Name**: Execution context
- **Maturity**: GA
- **Summary**: Shares per-call state across handlers through an ambient context scope.

## What It Does

`IExecutionContext` carries mediation-scoped state:
- `CancellationToken`
- `Items` key-value data
- `Tags`
- `MessageResult`
- `SuppressPostHandlers()`

`AmbientExecutionContext` stores the current context via `AsyncLocal<IExecutionContext?>`. Handlers access it statically inside mediation flow. `CreateScope` sets and restores ambient context for nested and async flows.

## Public Surface

```csharp
public sealed class ContextAwarePreHandler : ICommandPreHandler<CreateOrderCommand>
{
    public Task PreHandleAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        AmbientExecutionContext.Current.Items["tenant"] = "tenant-a";
        return Task.CompletedTask;
    }
}
```

| API | Role |
| --- | --- |
| `IExecutionContext.Items` | Shared per-call state |
| `IExecutionContext.Tags` | Effective routing tags |
| `IExecutionContext.CancellationToken` | Effective cancellation token |
| `IExecutionContext.MessageResult` | Result override/abort payload |
| `IExecutionContext.SuppressPostHandlers()` | Skips the post-handlers that have not run yet |
| `PipelineDirective.ShortCircuit(result, reason)` | Stops a pipeline from a short-circuiting pre-handler |
| `AmbientExecutionContext.Current` | Access current ambient context |
| `AmbientExecutionContext.HasCurrent` | Presence check |
| `AmbientExecutionContext.CreateScope(IExecutionContext)` | Scoped ambient context |
| `AmbientExecutionContext.ResetForTesting()` | Test-only context reset helper |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Messaging`

## Requires

- `mediator.handler-pipeline`
- `mediator.mediation-settings`

## Invariants

- Context is scoped to one mediation call.
- `Current` throws `NoExecutionContextException` when accessed outside scope.
- Result commands and queries aborted in pre-stage require a result object.
- `MessageResult` write in void command flow is ignored by strategy return path.

## Non-Goals

- Cross-request ambient data persistence.
- Distributed context propagation across processes.
- Replacing explicit message fields for business data.

## Observability

No context-specific meter or trace source is emitted.

Operational alternatives:
- Use `Items` to carry timing markers and emit logs in handlers.
- Validate context isolation with unit tests.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `CreateScope_dispose_and_ResetForTesting_should_restore_and_clear_context` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_ShouldPropagateContextItemsToAllHandlers` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_items_in_settings_propagates_context_correctly` | `LiteBus.Mediator.UnitTests` |
| `Send_TwoCommandsSequentially_OverrideDoesNotLeakBetweenCalls` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_ShouldRetainAmbientScopeUntilHandlerContinuationCompletes` | `LiteBus.Mediator.UnitTests` |

### Untested

- High-concurrency nested scopes across large fan-out event handlers.
- Very large `Items` payload memory pressure.

### Out-of-Scope

- Async local diagnostics outside LiteBus mediation.
- External tracing propagation protocol implementation.

## Deep Docs

- [Execution context](../../concepts/execution-context.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
