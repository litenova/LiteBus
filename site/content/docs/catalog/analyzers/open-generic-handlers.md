# Open Generic Handlers

## Header

- **ID**: `analyzers.open-generic-handler-shape`
- **Diagnostic**: `LB1005` (Error)
- **Maturity**: GA
- **Summary**: Reports open generic handler definitions that expose an unsupported generic arity for bare message-type handler shapes.

## Trigger Conditions

`LB1005` reports when all of the following are true:

- Handler type is generic.
- Handler type uses a bare message type parameter in the first handler interface type argument.
- Open generic handler definition exposes a type-parameter count other than 1, or exposes 2 without implementing a handler contract taking both of them in order.

Diagnostic locations include:

- Type declaration location.
- `typeof(...)` registration location when unsupported handler is referenced in `typeof` expression.

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

- Refactor an unsupported generic handler to one message type parameter, or to two where the second is bound by a typed handler contract such as `ICommandPostHandler<TCommand, TCommandResult>`.
- Keep auxiliary generic context in constructor dependencies or closed helper services.
- Do not suppress unless compatibility with a fixed external generic contract is required.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/UnsupportedOpenGenericHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `SupportedOpenGenericHandler_ProducesNoDiagnostic` | One-parameter open generic handler is valid |
| `UnsupportedOpenGenericHandler_ProducesDiagnostic` | Unsupported declaration reports `LB1005` |
| `TypeOfUnsupportedOpenGenericHandler_ProducesDiagnostic` | `typeof(InvalidLogger<,>)` usage also reports `LB1005` |
