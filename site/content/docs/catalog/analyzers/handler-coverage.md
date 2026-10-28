# Handler Coverage

## Header

- **ID**: `analyzers.missing-command-handler`, `analyzers.missing-query-handler`
- **Diagnostics**: `LB1008` (Error), `LB1009` (Error)
- **Maturity**: GA
- **Summary**: Reports command, query, and stream query message types declared in the analyzed compilation that do not have a main handler.

## Trigger Conditions

### LB1008 (Missing Command Handler)

The analyzer reports when a concrete `ICommand` or `ICommand<TResult>` type has no covering main command handler:

- No `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TResult>` for the type.
- No handler registered for a base command type that is assignable from the concrete type.
- No supported open generic main command handler that closes for the type (an arity-2 handler binds the message's declared result type and covers only messages that declare one).

### LB1009 (Missing Query Handler)

The analyzer reports when a concrete query type has no matching main query handler:

- `IQuery<TResult>` requires `IQueryHandler<TQuery, TResult>`.
- `IStreamQuery<TResult>` requires `IStreamQueryHandler<TQuery, TResult>`.
- Stream queries are checked against stream query handlers, not regular query handlers.
- No supported open generic main handler that closes for the type (an arity-2 handler binds the message's declared result type and covers only messages that declare one).

Message candidates are scoped to the analyzed assembly: a message from a referenced assembly is never reported, while handlers from eligible referenced assemblies still satisfy coverage. A handler that lives in a dependent project stays invisible; co-locate it with the message or suppress the diagnostic. See [Analyzers](../../reference/analyzers.md).

## Bad Example

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Queries.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;
public sealed record GetUserQuery(int UserId) : IQuery<string>;
```

Expected diagnostics:

- `LB1008` on `CreateUserCommand`
- `LB1009` on `GetUserQuery`

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Queries.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;
public sealed record GetUserQuery(int UserId) : IQuery<string>;

public sealed class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
{
    public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult("ok");
}
```

## Suppression Guidance

- Treat both diagnostics as correctness errors, not style warnings.
- Suppress only when handler registration happens outside analyzable source boundaries and that boundary is documented.
- Keep suppression local and include a comment with the registration path.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/MissingCommandHandlerAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/MissingQueryHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `CommandWithHandler_ProducesNoDiagnostic` | Covered command type does not report `LB1008` |
| `ResultCommandWithArityTwoOpenGenericHandler_ProducesNoDiagnostic` | Arity-2 open generic handler covers a result command |
| `VoidCommandWithOnlyArityTwoOpenGenericHandler_ProducesDiagnostic` | Void command with only an arity-2 handler reports `LB1008` |
| `CommandWithoutHandler_ProducesDiagnostic` | Unhandled command reports `LB1008` |
| `DerivedCommandWithBaseHandler_ProducesNoDiagnostic` | Base command handler satisfies derived command coverage |
| `CommandWithOpenGenericHandler_ProducesNoDiagnostic` | Open generic command handler satisfies coverage |
| `CommandInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Referenced assembly message with a local handler does not report `LB1008` |
| `CommandInReferencedAssemblyWithoutHandler_ProducesNoDiagnostic` | Referenced assembly command without handler is not reported |
| `CommandWithHandlerInReferencedAssembly_ProducesNoDiagnostic` | Handler in a referenced assembly satisfies coverage |
| `CommandWithOpenGenericHandlerInReferencedAssembly_ProducesNoDiagnostic` | Open generic handler in a referenced assembly satisfies coverage |
| `QueryWithHandler_ProducesNoDiagnostic` | Covered query type does not report `LB1009` |
| `ResultQueryWithArityTwoOpenGenericHandler_ProducesNoDiagnostic` | Arity-2 open generic handler covers a result query |
| `QueryWithoutHandler_ProducesDiagnostic` | Unhandled query reports `LB1009` |
| `StreamQueryWithHandler_ProducesNoDiagnostic` | Covered stream query does not report `LB1009` |
| `StreamQueryWithoutHandler_ProducesDiagnostic` | Unhandled stream query reports `LB1009` |
| `QueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Referenced assembly query with a local handler does not report `LB1009` |
| `QueryInReferencedAssemblyWithoutHandler_ProducesNoDiagnostic` | Referenced assembly query without handler is not reported |
| `StreamQueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Referenced assembly stream query with a local handler does not report `LB1009` |
| `StreamQueryInReferencedAssemblyWithoutHandler_ProducesNoDiagnostic` | Referenced assembly stream query without handler is not reported |
