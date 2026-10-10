# Execution Context

- **ID**: `mediator.execution-context`
- **Name**: Execution context
- **Maturity**: GA
- **Summary**: Shares per-call state across handlers through a scoped dependency or an ambient context scope.

## What It Does

`IExecutionContext` carries mediation-scoped state:
- `CancellationToken`
- `Items` string-keyed data
- `Data` type-keyed store (`IHandleContextData`)
- `Tags`
- `MessageResult`
- `SuppressPostHandlers()`

`IExecutionContext` is registered as a scoped dependency, so a handler declares it as a constructor parameter and the dependency appears in the type signature. `AmbientExecutionContext` stores the current context via `AsyncLocal<IExecutionContext?>` and remains the way to reach it from code that runs outside dependency injection. `CreateScope` sets and restores ambient context for nested and async flows.

Scoped here means per mediation. The mediator opens the ambient scope before it creates the dispatch scope, and there is one dispatch scope per mediation, so the scoped resolution returns the context of the mediation in flight.

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
| `IExecutionContext.Items` | Shared per-call state, string-keyed, for a key that comes from outside the process or a value that is a flag |
| `IExecutionContext.Data` | Type-keyed store for handing a resolved object from one stage to a later one |
| `IExecutionContext.Tags` | Effective routing tags |
| `IExecutionContext.CancellationToken` | Effective cancellation token |
| `IExecutionContext.MessageResult` | Result override written by a post-handler |
| `IExecutionContext.SuppressPostHandlers()` | Skips the post-handlers that have not run yet |
| `Verdict` | Refuses a message from a guard, with a reason and an optional code |
| `Shortcut` / `Shortcut<TResult>` | Answers a message from a shortcut, so the main handler never runs |
| `AmbientExecutionContext.Current` | Access current ambient context |
| `AmbientExecutionContext.HasCurrent` | Presence check |
| `AmbientExecutionContext.CreateScope(IExecutionContext)` | Scoped ambient context |
| `AmbientExecutionContext.ResetForTesting()` | Test-only context reset helper |
| `IHandleContextData.Set<T>` / `Get<T>` / `TryGet<T>` / `Contains<T>` / `Remove<T>` | One value per type |
| `IHandleContextData.Set<T>(key, value)` and the keyed reads | Several values of one type, for a mediation that legitimately holds two |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Messaging`

## Requires

- `mediator.handler-pipeline`
- `mediator.mediation-settings`

## Invariants

- Context is scoped to one mediation call.
- `Current` throws `NoExecutionContextException` when accessed outside scope, and so does resolving `IExecutionContext` from the container outside a mediation.
- The dispatch scope is a child of the root provider, not of the ambient request scope, so an application's scoped services in a handler are per mediation and are not the request's instances.
- `Data` holds one value per type in its unkeyed slot; a keyed entry and the unkeyed entry of the same type are separate slots and neither clears the other.
- A shortcut that stops a result-returning command or query supplies the result through its answer, not through the context; a refusal supplies none, and the value a refused caller receives comes from a registered refusal mapper.
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
