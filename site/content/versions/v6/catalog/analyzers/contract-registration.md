# Contract Registration

## Header

- **ID**: `analyzers.missing-contract-on-handled-type`, `analyzers.explicit-contract-registration`
- **Diagnostics**: `LB1007` (Warning), `LB1017` (Warning)
- **Maturity**: GA
- **Summary**: Guards durable command and event contract discovery by checking attribute and configuration registration paths.

## Rule Split

### LB1007

Reports for handled durable command or event message types when all of the following are true:

- Type is used by a command or event handler.
- Type does not declare `[MessageContract]`.
- Type is not explicitly registered by `Contracts.Register<T>()`, `Register(typeof(T), ...)`, `RegisterFromAssembly(...)`, or `AddFromAssembly(...)`.

### LB1017

Reports for attributed durable message types when all of the following are true:

- Type declares `[MessageContract(...)]`.
- Type is durable (`ICommand`, `ICommand<TResult>`, or `IEvent`).
- Type is not explicitly registered by `Register` or assembly scanning (`RegisterFromAssembly` or `AddFromAssembly`).

## Bad Example

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    public Task HandleAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1007` because `ProcessPaymentCommand` has no attribute and no registration.

## Good Example

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

[MessageContract("payments.process-payment", 1)]
public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;

public static class ModuleConfiguration
{
    public static void Configure(ContractsRegistry contracts)
    {
        contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
    }
}
```

This avoids both warnings:

- `LB1007` is satisfied by attribute and explicit registration.
- `LB1017` is satisfied by explicit registration.

## Suppression Guidance

- Prefer explicit registration over suppression because contract discovery predictability matters in durable paths.
- If registration happens in generated code or external composition, suppress at the narrowest scope and document where registration occurs.
- Do not suppress broadly in `.editorconfig` unless a whole project intentionally relies on runtime-only contract discovery.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/MissingMessageContractRegistrationAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ExplicitMessageContractRegistrationAnalyzerTests.cs`

| Test method | Verifies |
| --- | --- |
| `MessageWithContractAttribute_ProducesNoDiagnostic` | Attribute satisfies `LB1007` |
| `HandledMessageWithoutContractRegistration_ProducesDiagnostic` | Missing attribute and registration reports `LB1007` |
| `MessageRegisteredThroughTypeOfRegister_ProducesNoDiagnostic` | `Register(typeof(T), ...)` satisfies `LB1007` |
| `ClosedGenericHandledMessage_ProducesClosedTypeRegistrationSuggestion` | `LB1007` message uses closed generic display |
| `MessageRegisteredThroughContractsRegister_ProducesNoDiagnostic` | `Register<T>` satisfies `LB1007` |
| `HandledEventWithoutContractRegistration_ProducesDiagnostic` | Event handlers are included in `LB1007` |
| `HandledMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic` | `RegisterFromAssembly` satisfies `LB1007` |
| `QueryHandler_ProducesNoDiagnostic` | Query handlers are excluded from `LB1007` |
| `AttributedMessageWithoutExplicitRegistration_ProducesDiagnostic` | Attributed durable type without registration reports `LB1017` |
| `AttributedMessageWithExplicitRegister_ProducesNoDiagnostic` | Explicit `Register<T>` satisfies `LB1017` |
| `AttributedMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic` | `RegisterFromAssembly` satisfies `LB1017` |
