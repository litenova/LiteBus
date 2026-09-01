# Shortcut Contracts

## Header

- **ID**: `analyzers.untyped-shortcut-on-result-message`
- **Diagnostics**: `LB1019` (Warning)
- **Maturity**: GA
- **Summary**: Reports a shortcut that uses the untyped shortcut contract for a message that produces a result, where answering cannot supply the value the caller expects.

## Why It Exists

`ICommand<TResult>` derives from `ICommand`, and `IStreamQuery<TResult>` derives from `IQuery`. Both make the untyped shortcut contract compile for a message that does produce a result, so `ICommandShortcut<CreateProductCommand>` is accepted by the compiler even when `CreateProductCommand` implements `ICommand<Guid>`.

The untyped `Shortcut` carries no result. A shortcut that answers this way leaves the mediation with nothing to hand back, so answering fails with `LiteBusConfigurationException`.

This rule is one of three checks on the same mistake, at three different points. The compiler cannot help: C# has no way to write "`ICommand` but not `ICommand<T>`", which is the constraint that would close the hole.

| Point | Covers | Severity |
| --- | --- | --- |
| This rule, `LB1019` | The declaration, at build time | Warning, suppressible, absent without the analyzer package |
| Registration | A shortcut registered directly against a message whose main handler produces a result | `LiteBusConfigurationException`, always on |
| Dispatch | A shortcut registered against a base type that reaches one message beneath it that produces a result | `LiteBusConfigurationException`, names the shortcut |

Registration is where the guarantee actually lives, because it cannot be turned off. The rule still earns its place by moving the report to build time, where the fix is one keystroke away. The dispatch check covers the case neither of the other two can prove: a shortcut registered against `ICommand` is correct for every result-less command beneath it, so nothing before dispatch knows which message it will answer. See [Mediation Layer Design Rules](../../architecture/mediation-design.md).

For a message that produces a result the typed contract is a strict superset of the untyped one, which is why the rule reports the declaration rather than the individual call: the contract choice is the mistake, and the declaration is where the fix goes.

The guard contracts have no equivalent rule, and need none. A refusal does not owe the caller the value the handler would have produced, so `ICommandGuard<CreateProductCommand>` is correct for a result-returning command; refusing raises `LiteBusMessageDeniedException` by design. The typed guard exists only for applications that would rather hand back a failed result object.

## When It Reports

Reports for a type when all of the following are true:

- The type is not an interface and implements `IMessageShortcut<TMessage>`, directly or through `ICommandShortcut<TCommand>`, `IEventShortcut<TEvent>`, or another axis contract.
- `TMessage` is a concrete type rather than a type parameter.
- `TMessage` implements `ICommand<TResult>`, `IQuery<TResult>`, or `IStreamQuery<TResult>`.

The rule stays silent for a message that produces no result, for an event, for any guard, and for an open generic shortcut, whose message type is only known at dispatch.

## Bad Example

```csharp
using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

// LB1019: the answer cannot carry the Guid the caller expects
public sealed class SkipDuplicateProduct : ICommandShortcut<CreateProductCommand>
{
    public Task<Shortcut> TryAnswerAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Shortcut.Answer("the product already exists"));
}
```

## Good Example

```csharp
public sealed class SkipDuplicateProduct : ICommandShortcut<CreateProductCommand, Guid>
{
    public async Task<Shortcut<Guid>> TryAnswerAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await _products.FindIdByNameAsync(command.Name, cancellationToken);

        return existing is null
            ? Shortcut<Guid>.None
            : Shortcut<Guid>.Answer(existing.Value, "the product already exists");
    }
}
```

Refusing that same command is a different job and belongs to a guard, which the framework runs first. `ICommandGuard<CreateProductCommand>` needs no result type at all, because a refusal does not owe the caller the value the handler would have produced. Where the caller should receive a refusal value rather than an exception, that mapping lives in an `ICommandRefusalMapper<TCommand, TCommandResult>`, registered once for the message or for the whole axis.

## Packages

- `LiteBus.Analyzers`

## Test Coverage

| Test method | Project |
| --- | --- |
| `UntypedShortcutOnVoidCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `TypedShortcutOnResultCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedGuardOnResultCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `EventShortcut_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `OpenGenericShortcut_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedShortcutOnResultCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedShortcutOnQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedShortcutOnStreamQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |

## Deep Docs

- [The Handler Pipeline](../../concepts/handler-pipeline.md)
- [Troubleshooting](../../operations/troubleshooting.md)
