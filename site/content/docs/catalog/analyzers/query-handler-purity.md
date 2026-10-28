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

It reports when a dependency type in constructor parameters, fields, or properties matches or implements one of the impure APIs. Members inherited from base handler classes are inspected the same way as declared members. Ordinary method parameters are not dependencies, because a helper receives them from its caller rather than from injection, so they stay silent. The impure APIs are:

- `ICommandMediator`, `IEventMediator`, `IQueryMediator`
- `IInbox`, `ITransactionalInbox`, `ITransactionalInbox<T>`, `IInboxStore`, `ITransactionalInboxStore`
- `IOutbox`, `ITransactionalOutbox`, `ITransactionalOutbox<T>`, `IOutboxStore`, `ITransactionalOutboxStore`
- `ITransportPublisher`

The analyzer reports each impure dependency type once per handler.

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

Source: `tests/LiteBus.Analyzers.UnitTests/QueryHandlerImpurityAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/QueryHandlerImpurityAnalyzerRegressionTests.cs`

| Test method | Verifies |
| --- | --- |
| `PureQueryHandler_ProducesNoDiagnostic` | Pure query handler is accepted |
| `QueryHandlerWithCommandMediator_ProducesDiagnostic` | `ICommandMediator` dependency reports `LB1003` |
| `QueryHandlerWithInbox_ProducesDiagnostic` | `IInbox` dependency reports `LB1003` |
| `QueryHandlerWithTransactionalInbox_ProducesDiagnostic` | `IInboxStore` dependency reports `LB1003` |
| `QueryHandlerWithTransactionalInboxWriter_ProducesDiagnostic` | `ITransactionalInbox` dependency reports `LB1003` |
| `QueryHandlerWithTransactionalOutboxWriter_ProducesDiagnostic` | `ITransactionalOutbox` dependency reports `LB1003` |
| `QueryHandlerWithGenericTransactionalOutboxWriter_ProducesDiagnostic` | `ITransactionalOutbox<T>` dependency reports `LB1003` |
| `StreamQueryHandlerWithCommandMediator_ProducesDiagnostic` | Stream query handlers are covered |
| `QueryHandlerWithImpureField_ProducesDiagnostic` | Field dependency on `IEventMediator` reports `LB1003` |
| `QueryHandlerWithMessageTransport_ProducesDiagnostic` | `ITransportPublisher` dependency reports `LB1003` |
| `QueryHandlerWithOrdinaryMethodParameter_ProducesNoDiagnostic` | Ordinary method parameter is not an injected dependency |
| `QueryHandlerWithInheritedImpureProperty_ProducesDiagnostic` | Base-class property dependency reports `LB1003` |
