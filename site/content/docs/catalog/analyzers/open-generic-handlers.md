# Open Generic Handler Shape

## Header

- **ID**: `analyzers.open-generic-handler-shape`
- **Diagnostic**: `LB1005` (Error)
- **Maturity**: GA
- **Summary**: Reports open generic handler definitions with an unsupported type-parameter count for bare message-type handler shapes.

## Trigger Conditions

`LB1005` reports when every condition below holds:

- The handler type is generic.
- The handler type uses a bare message type parameter in the first handler interface type argument.
- The definition declares a type-parameter count other than 1, or declares 2 without implementing a handler contract that takes both parameters in order.

Diagnostic locations include:

- The type declaration location.
- `typeof(...)` usage when the unsupported handler is referenced in a `typeof` expression.

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed class InvalidLogger<TCommand, TContext> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1005` with arity `2`.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed class CommandLogger<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

## Suppression Guidance

- Refactor an unsupported generic handler to one message type parameter, or to two where the second is bound by a typed handler contract.
- Every two-argument handler contract binds the second parameter, not only post-handlers. The messaging-level set is `IMessageHandler<,>`, `IStreamMessageHandler<,>`, `IMessagePostHandler<,>`, `IMessageCompletionHandler<,>`, `IMessageErrorHandler<,>`, `IMessageShortcut<,>`, and `IMessageRefusalMapper<,>`. Every axis contract deriving from one of them counts too. A generic caching shortcut over `ICommandShortcut<TCommand, TCommandResult>` and a single refusal policy over `ICommandRefusalMapper<TCommand, TCommandResult>` are both supported. The check matches by shape rather than by name, so any contract taking the handler's two parameters in order can be closed.
- Keep auxiliary generic context in constructor dependencies or closed helper services.
- Do not suppress unless compatibility with a fixed external generic contract is required.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/UnsupportedOpenGenericHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `SupportedOpenGenericHandler_ProducesNoDiagnostic` | One-parameter open generic handler is valid |
| `UnsupportedOpenGenericHandler_ProducesDiagnostic` | Unsupported declaration reports `LB1005` |
| `TypeOfUnsupportedOpenGenericHandler_ProducesDiagnostic` | `typeof(InvalidLogger<,>)` usage also reports `LB1005` |
