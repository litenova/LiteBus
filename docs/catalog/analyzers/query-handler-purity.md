# Query Handler Purity

## Header

- **ID**: `analyzers.query-handler-purity`
- **Diagnostic**: `LB1003` (Warning)
- **Maturity**: GA
- **Summary**: Reports query and stream query handlers that depend on side-effecting mediator, inbox, outbox, or transport APIs.

## Trigger Conditions

`LB1003` runs on types implementing:

- `IQueryHandler<TQuery, TResult>`
- `IStreamQueryHandler<TQuery, TResult>`

It reports when a dependency type in constructor parameters, method parameters, fields, or properties matches or implements one of the impure APIs:

- `ICommandMediator`, `IEventMediator`, `IQueryMediator`
- `IInbox`, `ITransactionalInbox<T>`, `IInboxStore`, `ITransactionalInboxStore`
- `IOutbox`, `IOutboxStore`, `ITransactionalOutboxStore`
- `ITransportPublisher`

The analyzer reports each impure dependency metadata type once per handler.

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Queries.Abstractions;

public sealed record GetUserQuery(int UserId) : IQuery<string>;

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
{
    private readonly ICommandMediator _commandMediator;

    public GetUserQueryHandler(ICommandMediator commandMediator)
    {
        _commandMediator = commandMediator;
    }

    public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult("user");
}
```

Expected diagnostic:

- `LB1003` for dependency `LiteBus.Commands.Abstractions.ICommandMediator`.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

public sealed record GetUserQuery(int UserId) : IQuery<string>;

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, string>
{
    public Task<string> HandleAsync(GetUserQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult("user");
}
```

## Suppression Guidance

- Prefer splitting reads and writes rather than suppressing this warning.
- If a read path must trigger side effects for legacy reasons, isolate that behavior and document the reason next to the suppression.
- Keep suppression at handler scope, not project scope.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/QueryHandlerImpurityAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `PureQueryHandler_ProducesNoDiagnostic` | Pure query handler is accepted |
| `QueryHandlerWithCommandMediator_ProducesDiagnostic` | `ICommandMediator` dependency reports `LB1003` |
| `QueryHandlerWithInbox_ProducesDiagnostic` | `IInbox` dependency reports `LB1003` |
| `QueryHandlerWithTransactionalInbox_ProducesDiagnostic` | `IInboxStore` dependency reports `LB1003` |
| `StreamQueryHandlerWithCommandMediator_ProducesDiagnostic` | Stream query handlers are covered |
| `QueryHandlerWithImpureField_ProducesDiagnostic` | Field dependency on `IEventMediator` reports `LB1003` |
| `QueryHandlerWithMessageTransport_ProducesDiagnostic` | `ITransportPublisher` dependency reports `LB1003` |
