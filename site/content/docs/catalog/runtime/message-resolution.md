# Message Resolve Strategies

- **ID**: `runtime.message-resolution`
- **Name**: Message resolve strategies
- **Maturity**: GA
- **Summary**: The resolver finds message descriptors through `IMessageResolveStrategy`. The default strategy tries the exact type first and falls back to assignable types.

## What It Does

`ActualTypeOrFirstAssignableTypeMessageResolveStrategy` first checks exact type lookup, then scans assignable candidates and selects the most-derived match. Depth counts the base-type chain length plus the number of interfaces the registered type implements. For generic type definitions, depth is the chain length alone. Ambiguous ties raise `AmbiguousMessageResolveException`, whose `Candidates` names the registered types that tied.

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

- Exact lookup takes precedence over assignable fallback.
- When equal-depth matches are ambiguous, resolution throws.

## Non-Goals

- Resolution weighted by metadata.

## Observability

Resolution exposes no metric.

## Test Coverage

### Covered Use Cases

#### `PolymorphicDispatchTests.Send_SpecializedCommand_ShouldBeHandledByBaseCommandHandler`
- **Test kind**: Unit
- **Expected outcome**: assignable fallback resolves the base descriptor

#### `MessageMediatorTests.Mediate_WhenDescriptorCannotBeResolvedAfterOnTheSpotRegistration_ShouldThrowMessageDescriptorNotFoundException`
- **Test kind**: Unit
- **Expected outcome**: the unresolved strategy path throws the descriptor exception

#### `MessageResolveStrategyTests.Find_WhenMultipleAssignableDescriptorsShareDepth_ShouldThrowAmbiguousMessageResolveException`
- **Test kind**: Unit
- **Expected outcome**: equal-depth assignable candidates throw the ambiguity exception
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/UseCases/Messaging/MessageResolveStrategyTests.cs`

### Untested Use Cases

None currently tracked.

### Out-of-Scope Use Cases

- Resolution that merges multiple descriptors.
