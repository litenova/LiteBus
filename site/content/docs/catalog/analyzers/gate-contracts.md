# Gate Contracts

## Header

- **ID**: `analyzers.untyped-gate-on-result-message`
- **Diagnostics**: `LB1019` (Warning)
- **Maturity**: GA
- **Summary**: Reports a gate that uses the untyped gate contract for a message that produces a result, where a short-circuit cannot supply the value the caller expects.

## Why It Exists

`ICommand<TResult>` derives from `ICommand`, and `IStreamQuery<TResult>` derives from `IQuery`. Both make the untyped gate contract compile for a message that does produce a result, so `ICommandGate<CreateProductCommand>` is accepted by the compiler even when `CreateProductCommand` implements `ICommand<Guid>`.

The untyped `PipelineDirective` carries no result. A gate that stops the pipeline this way leaves the mediation with nothing to hand back, so `PipelineDirective.ShortCircuit` reaches the caller as `LiteBusConfigurationException` at the first dispatch that takes the branch. The exception message names the contract to use, but only after the code has shipped and run.

For a message that produces a result the typed contract is a strict superset of the untyped one. It can continue, short-circuit with a result, refuse with a result, and refuse without one. There is no decision the untyped contract expresses that the typed contract cannot, which is why the rule reports the declaration rather than the individual call: the contract choice is the mistake, and the declaration is where the fix goes.

## When It Reports

Reports for a type when all of the following are true:

- The type is not an interface and implements `IMessageGate<TMessage>`, directly or through `ICommandGate<TCommand>`, `IEventGate<TEvent>`, or another axis contract.
- `TMessage` is a concrete type rather than a type parameter.
- `TMessage` implements `ICommand<TResult>`, `IQuery<TResult>`, or `IStreamQuery<TResult>`.

The rule stays silent for a message that produces no result, for an event, and for an open generic gate, whose message type is only known at dispatch.

## Bad Example

```csharp
using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

// LB1019: the directive cannot carry the Guid the caller expects
public sealed class RejectDuplicateProduct : ICommandGate<CreateProductCommand>
{
    public Task<PipelineDirective> DecideAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PipelineDirective.ShortCircuit("the product already exists"));
}
```

## Good Example

```csharp
public sealed class RejectDuplicateProduct : ICommandGate<CreateProductCommand, Guid>
{
    public async Task<PipelineDirective<Guid>> DecideAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var existing = await _products.FindIdByNameAsync(command.Name, cancellationToken);

        return existing is null
            ? PipelineDirective<Guid>.Continue
            : PipelineDirective<Guid>.ShortCircuit(existing.Value, "the product already exists");
    }
}
```

Refusing a message that produces a result still has two shapes. `PipelineDirective<Guid>.Deny(reason, result)` hands the caller a refusal value, and `PipelineDirective<Guid>.Deny(reason)` raises `LiteBusMessageDeniedException` because there is nothing to hand back. Both are available on the typed contract, so moving to it costs no expressiveness.

## Packages

- `LiteBus.Analyzers`

## Test Coverage

| Test method | Project |
| --- | --- |
| `UntypedGateOnVoidCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `TypedGateOnResultCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `EventGate_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `OpenGenericGate_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedGateOnResultCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedGateOnQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UntypedGateOnStreamQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |

## Deep Docs

- [The Handler Pipeline](../../concepts/handler-pipeline.md)
- [Troubleshooting](../../operations/troubleshooting.md)
