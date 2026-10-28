# Open Generic Handlers

- **ID**: `mediator.open-generic-handlers`
- **Name**: Open generic handlers
- **Maturity**: GA
- **Summary**: Registers open generic handlers and closes them for each compatible concrete message type.
- **Guide**: [Open generic handlers](../../concepts/open-generic-handlers.md)

## What It Does

When a type like `OpenGenericPreHandler<TCommand> : ICommandPreHandler<TCommand>` is registered, the message registry stores it as an open generic handler definition. As concrete message types are registered, LiteBus closes the generic handler with `MakeGenericType` when the generic constraints match.

Closed descriptors are inserted into the normal handler collections and take part in priority, filtering, and pipeline semantics like non-generic handlers.

One parameter binds the message type. Two parameters are closed when the handler implements a typed contract over both in order (for example `ICommandPostHandler<TCommand, TCommandResult>`), with the second binding the result type the message declares through `IProducesResult<TMessageResult>`. A message declaring no result is skipped the way a constraint mismatch is. Any other arity throws `UnsupportedOpenGenericHandlerException`, and analyzer rule `LB1005` reports the same shape at compile time.

One open generic registration runs once per mediated message, whatever the registration order. An open generic closed over a base interface applies indirectly to every message beneath it, so `Guard<T> where T : ICommand` can close both as `Guard<ICommand>` and as `Guard<PlaceOrder>`. When both closures of one generic handler definition reach a message through the same contract, the direct closure wins and the indirect one is dropped for that message.

A different handler type that names the base interface is a separate registration and still runs, and a class that implements both the base and the concrete contract runs twice for that message, once per contract. The composition summary reports every such handler type on `LiteBusCompositionSummary.DuplicateStageCoverages`, and `Explain` marks each step with `IsClosedOpenGeneric`.

`MessageModuleBuilder.RequireExplicitOpenGenerics()` makes open generics found by a scan fail composition until each one is named with `Register(typeof(X<>))`. It is opt-in because scans have picked up open generics since v4. `LiteBusCompositionSummary` reports the closures either way.

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
| `MessageModuleBuilder.Register(Type)` | Registers an open generic handler definition |
| `CommandModuleBuilder.Register(Type)` | Registers a semantic open generic handler definition |
| `RegisterFromAssembly(Assembly)` | Auto-discovers open generic handlers |
| `MessageModuleBuilder.RequireExplicitOpenGenerics()` | Fails composition when a scan finds an open generic that was not named explicitly |
| `LiteBusCompositionSummary.OpenGenericHandlers` | The open generic closures per composition, reported whether or not explicit registration is required |
| `LiteBusCompositionSummary.DuplicateStageCoverages` | Handler types covering one message's stage twice, once directly and once through a base type |
| `IProducesResult<TMessageResult>` | Marker stating a message's result type, so a two-parameter handler can bind its second parameter |
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

- One parameter binds the message type. Two bind the message and result types only through a typed contract over both in order.
- Generic constraints are enforced before closing.
- Registration order does not matter. Handlers close for prior and future message registrations.
- Closed handlers receive the same routing and priority behavior as explicit handlers.
- When both closures of one generic handler definition reach a message through the same contract, the direct closure wins and the indirect one is dropped for that message, so one registration runs once per mediated message.

## Non-Goals

- Open generic handlers with three or more parameters, or with a second parameter that is not a result type bound by a typed contract.
- Automatic external library integration (for example FluentValidation adapters).
- Runtime re-closing after module composition completes.

## Observability

No dedicated metrics for open generic close count or close latency are emitted.

Operational alternatives:
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
| `An_open_generic_guard_closed_over_the_base_interface_before_the_command_runs_once` | `LiteBus.Mediator.UnitTests` |
| `A_class_implementing_the_base_and_the_concrete_guard_is_reported_and_runs_twice` | `LiteBus.Mediator.UnitTests` |
| `Open_generic_post_error_and_completion_handlers_run_once_beside_closed_base_handlers` | `LiteBus.Mediator.UnitTests` |
| `Single_run_shapes_are_not_reported` | `LiteBus.Mediator.UnitTests` |

### Untested

- Isolated open generic error-handler behavior for multiple message families.
- Open generic main handler scenarios with complex constraints and interface inheritance.

### Out-of-Scope

- Handlers with three or more parameters, or with an unbound second parameter.
- Hot path generic reflection (closing is startup/registration concern).

## References

- [Open generic handlers](../../concepts/open-generic-handlers.md)
- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
