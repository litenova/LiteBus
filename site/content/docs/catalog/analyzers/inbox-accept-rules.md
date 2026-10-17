# Inbox Accept Rules

## Header

- **ID**: `analyzers.inbox-result-command-guard`
- **Diagnostic**: `LB1004` (Error)
- **Maturity**: GA
- **Summary**: Reports result-bearing commands passed to inbox accept APIs because inbox persistence discards handler return values.

## Trigger Conditions

`LB1004` reports when:

- Invocation target is `AcceptAsync` or `AcceptBatchAsync`.
- Receiver type is `IInbox`, `ITransactionalInbox`, `ITransactionalInbox<TContext>`, or a type implementing any of them.
- Message type implements `ICommand<TResult>`.

Batch detection includes:

- Implicit arrays, explicit arrays, and collection expressions.
- `InboxAcceptItem<T>` and `InboxAcceptItem.From(...)` item shapes.
- Explicit generic calls such as `AcceptAsync<CreateUserCommand>(...)`.

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;

public sealed record CreateUserCommand(string Name) : ICommand<int>;

public sealed class UserService
{
    public Task ScheduleAsync(IInbox inbox, CreateUserCommand command, CancellationToken cancellationToken)
        => inbox.AcceptAsync(command, cancellationToken: cancellationToken);
}
```

Expected diagnostic:

- `LB1004` on `AcceptAsync(...)`.

## Good Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;

public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

public sealed class PaymentService
{
    public Task ScheduleAsync(IInbox inbox, ProcessPaymentCommand command, CancellationToken cancellationToken)
        => inbox.AcceptAsync(command, cancellationToken: cancellationToken);
}
```

## Suppression Guidance

- Do not suppress for production code paths, convert stored commands to result-less `ICommand`.
- If a result is required immediately, send with `ICommandMediator` instead of storing through inbox.
- Keep any suppression local to migrations where old message contracts cannot be changed yet.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/CommandWithResultScheduledToInboxAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `VoidCommandStoredInInbox_ProducesNoDiagnostic` | Result-less command is accepted |
| `CommandWithResultStoredInInbox_ProducesDiagnostic` | Result command in `AcceptAsync` reports `LB1004` |
| `ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic` | Explicit generic `AcceptAsync<T>` reports `LB1004` |
| `CommandWithResultStoredThroughInboxImplementation_ProducesDiagnostic` | Interface implementations of `IInbox` are recognized |
| `ResultCommandInCollectionExpressionBatch_ProducesDiagnostic` | Result command in an inline heterogeneous batch reports `LB1004` at the offending item |
| `ResultCommandInLocalSpreadBatch_ProducesDiagnostic` | Result command found through local initializers and collection spreads reports `LB1004` |
| `ResultCommandInTransactionalArrayBatch_ProducesDiagnostic` | Result command in an explicitly created array batch to a transactional inbox reports `LB1004` |
| `ResultCommandInLocalListBatch_ProducesDiagnostic` | Result command in a target-typed local list batch reports `LB1004` |
