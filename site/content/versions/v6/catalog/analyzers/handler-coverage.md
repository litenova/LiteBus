# Handler Coverage

## Header

- **ID**: `analyzers.missing-command-handler`, `analyzers.missing-query-handler`
- **Diagnostics**: `LB1008` (Error), `LB1009` (Error)
- **Maturity**: GA
- **Summary**: Reports command, query, and stream query message types that do not have a main handler in the analyzed compilation and eligible referenced assemblies.

## Trigger Conditions

### LB1008 (Missing Command Handler)

The analyzer reports when a concrete `ICommand` or `ICommand<TResult>` type has no covering main command handler:

- No `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TResult>` for the type.
- No handler registered for a base command type that is assignable from the concrete type.
- No supported open generic main command handler that closes for the type.

### LB1009 (Missing Query Handler)

The analyzer reports when a concrete query type has no matching main query handler:

- `IQuery<TResult>` requires `IQueryHandler<TQuery, TResult>`.
- `IStreamQuery<TResult>` requires `IStreamQueryHandler<TQuery, TResult>`.
- Stream queries are checked against stream query handlers, not regular query handlers.

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
| `CommandWithoutHandler_ProducesDiagnostic` | Unhandled command reports `LB1008` |
| `DerivedCommandWithBaseHandler_ProducesNoDiagnostic` | Base command handler satisfies derived command coverage |
| `CommandWithOpenGenericHandler_ProducesNoDiagnostic` | Open generic command handler satisfies coverage |
| `CommandInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Coverage resolves across referenced assembly message types |
| `CommandInReferencedAssemblyWithoutHandler_ProducesDiagnostic` | Referenced assembly command without handler reports `LB1008` |
| `QueryWithHandler_ProducesNoDiagnostic` | Covered query type does not report `LB1009` |
| `QueryWithoutHandler_ProducesDiagnostic` | Unhandled query reports `LB1009` |
| `StreamQueryWithHandler_ProducesNoDiagnostic` | Covered stream query does not report `LB1009` |
| `StreamQueryWithoutHandler_ProducesDiagnostic` | Unhandled stream query reports `LB1009` |
| `QueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Referenced assembly query coverage is recognized |
| `QueryInReferencedAssemblyWithoutHandler_ProducesDiagnostic` | Referenced assembly query without handler reports `LB1009` |
| `StreamQueryInReferencedAssemblyWithHandler_ProducesNoDiagnostic` | Referenced assembly stream query coverage is recognized |
| `StreamQueryInReferencedAssemblyWithoutHandler_ProducesDiagnostic` | Referenced assembly stream query without handler reports `LB1009` |
