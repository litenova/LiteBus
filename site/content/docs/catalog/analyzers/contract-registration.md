# Contract Registration

## Header

- **ID**: `analyzers.missing-contract-on-handled-type`, `analyzers.explicit-contract-registration`
- **Diagnostics**: `LB1007` (Warning), `LB1017` (Warning), `LB1029` (Warning)
- **Maturity**: GA
- **Summary**: Reports handled durable commands and events with no contract attribute or registration, attributed durable types with no explicit registration, and unattributed durable types in assemblies that name a registering assembly.

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

### LB1029

The rule reports a durable message type without `[MessageContract]` when every condition below holds:

- The analyzed assembly carries `[assembly: MessageContractsRegisteredIn(...)]` naming at least one registering assembly.
- The type is durable (`ICommand`, `ICommand<TResult>`, or `IEvent`).
- The type declares no `[MessageContract]`.

Queries are not durable, so they never report. Open generic type definitions never report. The diagnostic names the registering assemblies declared on the analyzed assembly. A partial type with two declarations reports once, at its first location.

Only contract surfaces satisfy either registration rule.

`Register` on the message, command, event, or query axis builders records message and handler types, so it does not count. `RegisterFromAssembly` counts everywhere except on the command, event, and query axis builders. A `Register` extension method whose receiver is a contract writer surface counts as well.

An assembly scan counts when its argument is `typeof(T)`, `typeof(T).Assembly`, a local initialized to either form, or `Assembly.GetExecutingAssembly()` / `Assembly.GetCallingAssembly()`. Any other shape does not count.

All three rules analyze one compilation. Only handlers and message types declared in the analyzed assembly are candidates, and only registrations invoked in the analyzed compilation satisfy LB1007 and LB1017. A registration that lives in a dependent project stays invisible, so co-locate it with the handler or attributed type, name the registering assembly as described below, or suppress the diagnostic. A partial type with two declarations reports once, at its first location. See [Analyzers](../../reference/analyzers.md).

## Cross-Assembly Registration

A modular monolith keeps published contracts in a small assembly that other modules reference, and registers them where the owning module composes. Without help, LB1017 reports every contract in the contracts assembly, and LB1007 reports every handler of those contracts in a consuming module, because each compilation only sees its own registration calls.

A contracts assembly names its registering assembly with an assembly attribute:

```csharp
using LiteBus.Messaging.Abstractions;

[assembly: MessageContractsRegisteredIn("Ledger")]
```

With the attribute in place:

- In every compilation except `Ledger`, a message type declared in the attributed assembly is treated as registered. LB1017 stays silent in the contracts assembly, and LB1007 stays silent for handlers of those contracts in consuming modules.
- In the `Ledger` compilation, LB1017 reports every `[MessageContract]` type of a referenced assembly that names `Ledger` and has no `Contracts.Register<T>` or `RegisterFromAssembly` covering it. The diagnostic carries no source location because the type is declared in another assembly; the message names the contract type.
- In the attributed compilation itself, LB1029 reports every durable command or event without `[MessageContract]`. Consuming compilations trust the named assembly for every type declared there, and the named compilation checks only attributed types, so without this rule an unattributed type would never be checked anywhere.

Prefer the attribute over suppressing LB1017 in each contracts project, so the registering compilation keeps the check.

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

In an assembly that names a registering assembly, an unattributed durable type reports `LB1029` instead:

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

[assembly: MessageContractsRegisteredIn("Ledger")]

public sealed record ProcessPaymentCommand(int PaymentId) : ICommand;
```

Expected diagnostic:

- `LB1029` because `ProcessPaymentCommand` carries no `[MessageContract]` in an assembly that delegates registration to `Ledger`.

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

This avoids all three warnings:

- `LB1007` is satisfied by the attribute and the explicit registration.
- `LB1017` is satisfied by the explicit registration.
- `LB1029` is satisfied by the attribute wherever the type is declared.

## Suppression Guidance

- Prefer explicit registration over suppression because predictable contract discovery matters in durable paths.
- If contracts live in a referenced assembly, prefer `[assembly: MessageContractsRegisteredIn("...")]` over suppression, so the registering compilation still reports missing registrations.
- If the attributed assembly reports `LB1029`, prefer adding `[MessageContract]` to the unattributed durable type, so the registering compilation is checked against it.
- If registration happens in generated code or external composition, suppress at the narrowest scope and document where registration occurs.
- Do not suppress broadly in `.editorconfig` unless a whole project intentionally relies on runtime-only contract discovery.

## Test Coverage

Source: `tests/LiteBus.Analyzers.UnitTests/MissingMessageContractRegistrationAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ExplicitMessageContractRegistrationAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ExplicitMessageContractRegistrationAnalyzerRegressionTests.cs`, `tests/LiteBus.Analyzers.UnitTests/ContractRegistrationCompilationEndTests.cs`, `tests/LiteBus.Analyzers.UnitTests/CrossAssemblyContractRegistrationTests.cs`, `tests/LiteBus.Analyzers.UnitTests/UnattributedContractAnalyzerTests.cs`

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
| `AttributedMessageInAssemblyNamingAnotherAssembly_ProducesNoDiagnostic` | Assembly attribute naming another assembly satisfies `LB1017` |
| `AttributedMessageInAssemblyNamingCurrentCompilation_ProducesDiagnostic` | Assembly attribute naming the analyzed compilation still reports `LB1017` |
| `UnregisteredReferencedContractNamingCurrentCompilation_ProducesUnlocatedDiagnostic` | Unregistered referenced contract naming the compilation reports unlocated `LB1017` |
| `ReferencedContractNamingCurrentCompilationWithExplicitRegister_ProducesNoDiagnostic` | `Register<T>` satisfies `LB1017` for a referenced contract |
| `ReferencedContractNamingCurrentCompilationWithAssemblyScan_ProducesNoDiagnostic` | `RegisterFromAssembly` satisfies `LB1017` for a referenced contract |
| `ReferencedContractNamingAnotherAssembly_ProducesNoDiagnostic` | Referenced contract naming another assembly is not reported |
| `ReferencedContractWithoutRegisteringAssemblyAttribute_ProducesNoDiagnostic` | Referenced contract without the attribute is not reported |
| `NonDurableReferencedContractNamingCurrentCompilation_ProducesNoDiagnostic` | Attributed non-message referenced type is not reported |
| `TwoUnregisteredReferencedContractsNamingCurrentCompilation_ProduceTwoUnlocatedDiagnostics` | Every unregistered referenced contract is reported |
| `HandlerOfMessageRegisteredInAnotherAssembly_ProducesNoDiagnostic` | Assembly attribute naming another assembly satisfies `LB1007` |
| `HandlerOfMessageWithoutRegisteringAssemblyAttribute_ProducesDiagnostic` | Referenced message without the attribute still reports `LB1007` |
| `HandlerOfMessageRegisteredInCurrentCompilationWithoutRegistration_ProducesDiagnostic` | Handler in the named compilation without registration reports `LB1007` |
| `HandlerOfMessageRegisteredInCurrentCompilationWithExplicitRegister_ProducesNoDiagnostic` | `Register<T>` satisfies `LB1007` in the named compilation |
| `UnattributedCommandInRegisteredAssembly_ProducesDiagnostic` | Unattributed command in an attributed assembly reports `LB1029` |
| `AttributedCommandInRegisteredAssembly_ProducesNoDiagnostic` | Attributed command in an attributed assembly is accepted |
| `UnattributedEventInRegisteredAssembly_ProducesDiagnostic` | Unattributed event in an attributed assembly reports `LB1029` |
| `UnattributedCommandWithoutRegisteredAssembly_ProducesNoDiagnostic` | Unattributed command without the assembly attribute is not reported |
| `UnattributedQueryInRegisteredAssembly_ProducesNoDiagnostic` | Queries are not durable and never report `LB1029` |
| `NonMessageTypeInRegisteredAssembly_ProducesNoDiagnostic` | Non-message types never report `LB1029` |
| `UnattributedCommandNamingTwoAssemblies_ProducesDiagnosticWithBothNames` | The diagnostic names every registering assembly |
| `OpenGenericCommandInRegisteredAssembly_ProducesNoDiagnostic` | Open generic type definitions never report `LB1029` |
| `AbstractCommandInRegisteredAssembly_ProducesDiagnostic` | Abstract commands report `LB1029` |
| `PartialUnattributedCommandAcrossTwoFiles_ReportsOnceAtFirstDeclaration` | Partial unattributed type reports once at its first declaration |
