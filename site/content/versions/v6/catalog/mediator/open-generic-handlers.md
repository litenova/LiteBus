# Open Generic Handlers

- **ID**: `mediator.open-generic-handlers`
- **Name**: Open generic handlers
- **Maturity**: GA
- **Summary**: Registers one-parameter open generic handlers and closes them for each compatible concrete message type.

## What It Does

When a type like `OpenGenericPreHandler<TCommand> : ICommandPreHandler<TCommand>` is registered, message registry stores it as an open generic handler definition. As concrete message types are registered, LiteBus closes the generic handler with `MakeGenericType` if generic constraints match.

Closed descriptors are inserted into normal handler collections and participate in priority, filtering, and pipeline semantics like non-generic handlers.

Unsupported open generic arity is rejected (`UnsupportedOpenGenericHandlerException`), and analyzer rule `LB1005` covers the same shape at compile-time.

## Public Surface

```csharp
public sealed class CommandLoggingPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// Registration
builder.Register(typeof(CommandLoggingPreHandler<>));
```

| API | Role |
| --- | --- |
| `MessageModuleBuilder.Register(Type)` | Registers open generic handler definition |
| `CommandModuleBuilder.Register(Type)` | Registers semantic open generic handler definition |
| `RegisterFromAssembly(Assembly)` | Auto-discovers open generic handlers |
| `UnsupportedOpenGenericHandlerException` | Runtime guard for unsupported open generic shape |
| Analyzer `LB1005` | Compile-time warning for unsupported open generic shape |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`
- Semantic packages for concrete handler interfaces

## Requires

- `mediator.module-registration`
- `mediator.handler-pipeline`
- `mediator.generic-messages`

## Invariants

- Open generic handler shape supports exactly one type parameter.
- Generic constraints are enforced before closing.
- Registration order does not matter; handlers close for prior and future message registrations.
- Closed handlers receive the same routing and priority behavior as explicit handlers.

## Non-Goals

- Multi-parameter open generic handler support.
- Automatic external library integration (for example FluentValidation adapters).
- Runtime re-closing after module composition completes.

## Observability

No dedicated metrics for open generic close count or close latency exist.

Available signals:
- Runtime startup failure with `UnsupportedOpenGenericHandlerException`.
- Analyzer signal `LB1005` in `LiteBus.Analyzers`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Send_WithOpenGenericPreHandler_ShouldExecuteForConcreteCommand` | `LiteBus.Mediator.UnitTests` |
| `Send_WithOpenGenericPostHandler_ShouldExecuteForConcreteCommand` | `LiteBus.Mediator.UnitTests` |
| `Send_WithOpenGenericPreAndPostHandler_ShouldExecuteInCorrectOrder` | `LiteBus.Mediator.UnitTests` |
| `Send_OpenGenericRegisteredBeforeCommand_ShouldStillApply` | `LiteBus.Mediator.UnitTests` |
| `Send_OpenGenericRegisteredAfterCommand_ShouldStillApply` | `LiteBus.Mediator.UnitTests` |
| `RegisterFromAssembly_DiscoversOpenGenericHandlers_WithoutExplicitRegistration` | `LiteBus.Mediator.UnitTests` |

### Untested

- Isolated open generic error-handler behavior for multiple message families.
- Open generic main handler scenarios with complex constraints and interface inheritance.

### Out-of-Scope

- Multi-parameter open generic handler support.
- Hot path generic reflection (closing is startup/registration concern).

## Deep Docs

- [Open generic handlers](../../concepts/open-generic-handlers.md)
- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
