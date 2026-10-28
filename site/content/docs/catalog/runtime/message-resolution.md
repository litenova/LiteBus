# Message Resolve Strategies

- **ID**: `runtime.message-resolution`
- **Name**: Message resolve strategies
- **Maturity**: GA
- **Summary**: Resolves message descriptors using `IMessageResolveStrategy`, with default exact-first assignable fallback.

## What It Does

`ActualTypeOrFirstAssignableTypeMessageResolveStrategy` first checks exact type lookup, then scans assignable candidates and selects the most-derived match. Depth is the base-type chain length plus the registered type's implemented interface count, with generic type definitions contributing only the chain length. Ambiguous ties raise `AmbiguousMessageResolveException`, whose `Candidates` names the registered types that tied.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageResolveStrategy.Find(Type, IMessageReader)` | Strategy contract |
| `ActualTypeOrFirstAssignableTypeMessageResolveStrategy` | Default resolver |
| `AmbiguousMessageResolveException.Candidates` | The equally specific registered types that collided |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-registry`

## Invariants

- Exact lookup wins over assignable fallback.
- Ambiguous equal-depth matches throw.

## Non-Goals

- Weighted metadata-based resolution.

## Observability

No dedicated metric.

## Test Coverage

### Covered Use Cases

#### `PolymorphicDispatchTests.Send_SpecializedCommand_ShouldBeHandledByBaseCommandHandler`
- **Test kind**: Unit
- **Expected outcome**: assignable fallback resolves base descriptor

#### `MessageMediatorTests.Mediate_WhenDescriptorCannotBeResolvedAfterOnTheSpotRegistration_ShouldThrowMessageDescriptorNotFoundException`
- **Test kind**: Unit
- **Expected outcome**: unresolved strategy path fails with descriptor exception

#### `MessageResolveStrategyTests.Find_WhenMultipleAssignableDescriptorsShareDepth_ShouldThrowAmbiguousMessageResolveException`
- **Test kind**: Unit
- **Expected outcome**: equal-depth assignable candidates raise the ambiguity exception
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/UseCases/Messaging/MessageResolveStrategyTests.cs`

### Untested Use Cases

None currently tracked.

### Out-of-Scope Use Cases

- Multi-descriptor merge resolution.
