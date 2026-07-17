# Handler Duplicates

## Header

- **ID**: `analyzers.duplicate-command-handler`, `analyzers.duplicate-query-handler`
- **Diagnostics**: `LB1001` (Error), `LB1010` (Error)
- **Maturity**: GA
- **Summary**: Reports when more than one main handler is discovered for the same command, query, or stream query message type.

## Trigger Conditions

### LB1001 (Duplicate Command Handler)

Reports when two or more handlers are registered for one command message type in the command pipeline:

- `ICommandHandler<TCommand>`
- `ICommandHandler<TCommand, TResult>`

### LB1010 (Duplicate Query Handler)

Reports when two or more handlers are registered for one query type in either pipeline:

- Query pipeline: `IQueryHandler<TQuery, TResult>`
- Stream query pipeline: `IStreamQueryHandler<TQuery, TResult>`

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand;

public sealed class FirstCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SecondCreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1001` on the second registration point.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

public sealed record GetUserQuery(int UserId) : IQuery<string>;

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
{
    public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult("Ada");
}
```

## Suppression Guidance

- Do not suppress by default, this rule prevents runtime `MultipleHandlerFoundException` style ambiguity.
- If multiple handlers are intentional, split responsibilities into pre, post, or error handlers, or use event handlers.
- Keep one main handler per command and one per query type.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/DuplicateCommandHandlerAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/DuplicateQueryHandlerAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `SingleCommandHandler_ProducesNoDiagnostic` | Single command handler is valid |
| `DuplicateCommandHandlers_ProduceDiagnostic` | Duplicate command handlers report `LB1001` |
| `DuplicateCommandHandlersWithResult_ProduceDiagnostic` | Duplicate result command handlers report `LB1001` |
| `SingleQueryHandler_ProducesNoDiagnostic` | Single query handler is valid |
| `DuplicateQueryHandlers_ProduceDiagnostic` | Duplicate query handlers report `LB1010` |

Untested in this suite:

- Duplicate stream query handler fixture for `LB1010` (logic is implemented, dedicated stream duplicate fixture is not present).
