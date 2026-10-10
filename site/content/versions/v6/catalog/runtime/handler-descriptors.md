# Handler Descriptor Model

- **ID**: `runtime.handler-descriptors`
- **Name**: Handler descriptor model
- **Maturity**: GA
- **Summary**: Defines descriptor contracts for message handlers, stages, priorities, tags, and registration sequence.

## What It Does

Descriptor contracts in `LiteBus.Messaging.Abstractions` connect registration and runtime execution. `IMessageDescriptor` groups handlers by stage and direct/indirect applicability. `IHandlerDescriptor` provides metadata used by filtering and ordering.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageDescriptor` | Message descriptor with stage-specific handler collections |
| `IHandlerDescriptor` | Base handler metadata contract |
| `IMainHandlerDescriptor` | Main handler descriptor |
| `IPreHandlerDescriptor` | Pre-handler descriptor |
| `IPostHandlerDescriptor` | Post-handler descriptor |
| `IErrorHandlerDescriptor` | Error-handler descriptor |

## Packages

- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-registry`
- `runtime.mediation-strategies`

## Invariants

- `RegistrationSequence` is a stable registration-order tiebreaker.
- Descriptor message types for generics are normalized.

## Non-Goals

- Persistent descriptor storage format.

## Observability

No direct telemetry; descriptors are observable through mediation behavior.

## Test Coverage

### Covered Use Cases

#### `MessageRegistryTests.Register_Handler_ShouldRegisterHandlerAndMessage`
- **Test kind**: Unit
- **Expected outcome**: descriptors are created and linked

#### `PredicateFilteringTests.Publish_Event_WithPredicate_ShouldExecuteOnlyMatchingHandlers`
- **Test kind**: Unit
- **Expected outcome**: descriptor-based predicate filtering works

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Heavy tag-collision scenarios | Low | Core filter behavior is covered |

### Out-of-Scope Use Cases

- Reflection-free descriptor generation.
