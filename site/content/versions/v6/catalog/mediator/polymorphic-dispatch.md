# Polymorphic Dispatch

- **ID**: `mediator.polymorphic-dispatch`
- **Name**: Polymorphic dispatch
- **Maturity**: GA
- **Summary**: Resolves base-type and interface handlers for derived messages and prevents ambiguous descriptor selection.

## What It Does

Polymorphic dispatch uses `ActualTypeOrFirstAssignableTypeMessageResolveStrategy`:
1. Try exact type (or generic type definition for constructed generics).
2. Otherwise pick the most-derived assignable descriptor.
3. Throw `AmbiguousMessageResolveException` when multiple assignable descriptors share top depth.

Pipeline stages also run indirect handlers for assignable base/interface types. This enables cross-cutting behavior on shared marker interfaces while preserving strict single-handler rules for command/query main handlers.

## Public Surface

```csharp
public abstract record BasePolymorphicCommand : ICommand;
public sealed record SpecializedPolymorphicCommand : BasePolymorphicCommand;

public sealed class BasePolymorphicCommandHandler : ICommandHandler<BasePolymorphicCommand>
{
    public Task HandleAsync(BasePolymorphicCommand message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

| API | Role |
| --- | --- |
| `ActualTypeOrFirstAssignableTypeMessageResolveStrategy` | Descriptor resolution strategy |
| `AmbiguousMessageResolveException` | Ambiguous polymorphic match guard |
| Indirect handler collections on message descriptors | Base/interface pre, post, error, and main handler linkage |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`
- Semantic mediator packages (commands, queries, events)

## Requires

- `mediator.handler-pipeline`
- `mediator.module-registration`

## Invariants

- Exact descriptor match wins before assignable candidates.
- Ambiguous highest-depth assignable candidates throw instead of choosing arbitrarily.
- Single-handler mediators still enforce one main handler after polymorphic resolution.
- Generic message resolution normalizes by generic type definition first.

## Non-Goals

- Multiple dispatch with user-defined tie-break plugins in semantic mediators.
- Runtime fallback to arbitrary handler when ambiguity exists.
- Cross-process polymorphic routing.

## Observability

No dedicated polymorphic resolution counters or traces exist.

Operational alternatives:
- Use startup tests to assert expected resolver behavior.
- Log and fail fast on `AmbiguousMessageResolveException`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Send_SpecializedCommand_ShouldBeHandledByBaseCommandHandler` | `LiteBus.Mediator.UnitTests` |
| `Find_WhenMidTypeAndBaseTypeAreRegistered_ShouldPreferMostDerivedAssignableDescriptor` | `LiteBus.Mediator.UnitTests` |
| `Find_WhenMultipleAssignableDescriptorsShareDepth_ShouldThrowAmbiguousMessageResolveException` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamQuery_WithIndirectHandler_ShouldUseBaseTypeHandler` | `LiteBus.Mediator.UnitTests` |

### Untested

- Ambiguity behavior with deeply nested generic base types across many assemblies.
- Performance characteristics for very large assignable descriptor sets.

### Out-of-Scope

- Automatic ambiguity conflict resolution policies.
- Runtime descriptor ranking configuration.

## Deep Docs

- [Polymorphic dispatch](../../concepts/polymorphic-dispatch.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
