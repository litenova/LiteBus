# Contract Registration

## Header

- **ID**: `analyzers.missing-contract-on-handled-type`, `analyzers.explicit-contract-registration`
- **Diagnostics**: `LB1007` (Warning), `LB1017` (Warning)
- **Maturity**: GA
- **Summary**: Reports handled durable commands and events with no contract attribute or registration, and attributed durable types with no explicit registration.

## Rule Split

### LB1007

The rule reports a handled durable command or event message type when every condition below holds:

- A command or event handler uses the type.
- The type does not declare `[MessageContract]`.
- The type is not explicitly registered by `Contracts.Register<T>()`, `Register(typeof(T), ...)`, `RegisterFromAssembly(...)`, or `AddFromAssembly(...)`.

### LB1017

The rule reports an attributed durable message type when every condition below holds:

- The type declares `[MessageContract(...)]`.
- The type is durable (`ICommand`, `ICommand<TResult>`, or `IEvent`).
- The type is not explicitly registered by `Register` or assembly scanning (`RegisterFromAssembly` or `AddFromAssembly`).

Only contract surfaces satisfy either rule.

`Register` on the message, command, event, or query axis builders records message and handler types, so it does not count. `RegisterFromAssembly` counts everywhere except on the command, event, and query axis builders. A `Register` extension method whose receiver is a contract writer surface counts as well.

An assembly scan counts when its argument is `typeof(T)`, `typeof(T).Assembly`, a local initialized to either form, or `Assembly.GetExecutingAssembly()` / `Assembly.GetCallingAssembly()`. Any other shape does not count.

Both rules analyze one compilation. Only handlers and attributed types declared in the analyzed assembly are candidates, and only registrations invoked in the analyzed compilation satisfy the rules. A registration that lives in a dependent project stays invisible, so co-locate it with the handler or attributed type, or suppress the diagnostic. A partial type with two declarations reports once, at its first location. See [Analyzers](../../reference/analyzers.md).

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
    public static void Configure(IContractWriter contracts)
    {
        contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
    }
}
```

This avoids both warnings:

- `LB1007` is satisfied by the attribute and the explicit registration.
- `LB1017` is satisfied by the explicit registration.

## Suppression Guidance

- Prefer explicit registration over suppression because predictable contract discovery matters in durable paths.
- If registration happens in generated code or external composition, suppress at the narrowest scope and document where registration occurs.
- Do not suppress broadly in `.editorconfig` unless a whole project intentionally relies on runtime-only contract discovery.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/MissingMessageContractRegistrationAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ExplicitMessageContractRegistrationAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ExplicitMessageContractRegistrationAnalyzerRegressionTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ContractRegistrationCompilationEndTests.cs`

| Test method | Verifies |
| --- | --- |
| `MessageWithContractAttribute_ProducesNoDiagnostic` | Attribute satisfies `LB1007` |
| `HandledMessageWithoutContractRegistration_ProducesDiagnostic` | Missing attribute and registration reports `LB1007` |
| `MessageRegisteredThroughTypeOfRegister_ProducesNoDiagnostic` | `Register(typeof(T), ...)` satisfies `LB1007` |
| `ClosedGenericHandledMessage_ProducesClosedTypeRegistrationSuggestion` | `LB1007` message uses closed generic display |
| `MessageRegisteredThroughContractsRegister_ProducesNoDiagnostic` | `Register<T>` satisfies `LB1007` |
| `HandledEventWithoutContractRegistration_ProducesDiagnostic` | Event handlers are included in `LB1007` |
| `HandledMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic` | `RegisterFromAssembly` satisfies `LB1007` |
| `HandledMessageCoveredByExecutingAssemblyScan_ProducesNoDiagnostic` | `Assembly.GetExecutingAssembly()` scan satisfies `LB1007` |
| `HandledMessageCoveredByLocalAssemblyVariable_ProducesNoDiagnostic` | Scan through a `typeof(T).Assembly` local satisfies `LB1007` |
| `QueryHandler_ProducesNoDiagnostic` | Query handlers are excluded from `LB1007` |
| `HandledMessageRegisteredOnlyThroughScanShapedRegistry_ProducesDiagnostic` | Same-shaped `Register` with a scan does not satisfy `LB1007` |
| `AttributedMessageWithoutExplicitRegistration_ProducesDiagnostic` | Attributed durable type without registration reports `LB1017` |
| `AttributedMessageWithExplicitRegister_ProducesNoDiagnostic` | Explicit `Register<T>` satisfies `LB1017` |
| `AttributedMessageRegisteredThroughWriterExtension_ProducesNoDiagnostic` | `Register` extension on `IContractWriter` satisfies `LB1017` |
| `AttributedMessageCoveredByRegisterFromAssembly_ProducesNoDiagnostic` | `RegisterFromAssembly` satisfies `LB1017` |
| `AttributedMessageCoveredByExecutingAssemblyScan_ProducesNoDiagnostic` | `Assembly.GetExecutingAssembly()` scan satisfies `LB1017` |
| `AttributedMessageCoveredByLocalAssemblyVariable_ProducesNoDiagnostic` | Scan through a `typeof(T).Assembly` local satisfies `LB1017` |
| `AttributedMessageRegisteredOnlyThroughScanShapedRegistry_ProducesDiagnostic` | Same-shaped `Register` with a scan does not satisfy `LB1017` |
| `PartialAttributedMessageAcrossTwoFiles_ReportsOnceAtFirstDeclaration` | Partial attributed type reports once at its first declaration |
| `HandlerInReferencedAssemblyWithoutRegistration_ProducesNoDiagnostic` | Referenced assembly handler without registration is not reported |
