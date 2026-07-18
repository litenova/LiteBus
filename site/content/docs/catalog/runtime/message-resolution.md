# Message Resolve Strategies

- **ID**: `runtime.message-resolution`
- **Name**: Message resolve strategies
- **Maturity**: GA
- **Summary**: Resolves message descriptors using `IMessageResolveStrategy`, with default exact-first assignable fallback.

## What It Does

`ActualTypeOrFirstAssignableTypeMessageResolveStrategy` first checks exact type lookup, then scans assignable candidates and selects the most-derived match. Ambiguous ties raise `AmbiguousMessageResolveException`.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageResolveStrategy.Find(Type, IMessageReader)` | Strategy contract |
| `ActualTypeOrFirstAssignableTypeMessageResolveStrategy` | Default resolver |

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

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Explicit ambiguity tie coverage | Medium | Guard exists, dedicated tie test is limited |

### Out-of-Scope Use Cases

- Multi-descriptor merge resolution.
