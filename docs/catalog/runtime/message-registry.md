# Message and Handler Type Registry

- **ID**: `runtime.message-registry`
- **Name**: Message and handler type registry
- **Maturity**: GA
- **Summary**: Registers message and handler CLR types, builds descriptors, and supports open-generic handler expansion.

## What It Does

`MessageRegistry` implements `IMessageRegistry` (`IMessageWriter` and `IMessageReader`). It keeps exact message descriptor lookup, ordered handler descriptors, and open-generic handler templates that are closed against concrete messages.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageWriter.Register(Type)` | Registers message or handler type |
| `IMessageReader.Find(Type)` | Exact-type descriptor lookup |
| `IMessageReader.Handlers` | Ordered handler descriptors |
| `IMessageReader.Count` | Count of committed message descriptors |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.handler-descriptors`
- `runtime.message-resolution`

## Invariants

- Duplicate type registration is idempotent.
- Generic message types are normalized for lookup.
- Unsupported open-generic handler shapes fail fast.

## Non-Goals

- Persisted registry storage.

## Observability

No dedicated telemetry.

## Test Coverage

### Covered Use Cases

#### `MessageRegistryTests.Register_OpenGenericHandler_ShouldLinkToExistingConcreteMessageType`
- **Test kind**: Unit
- **Expected outcome**: open generic handler is closed and linked

#### `MessageRegistryTests.Register_ConcreteMessageAfterOpenGenericHandler_ShouldLinkOpenGenericHandler`
- **Test kind**: Unit
- **Expected outcome**: later concrete message receives closed handler

#### `MessageRegistryTests.Find_ExactType_ShouldReturnDescriptorInConstantTime`
- **Test kind**: Unit
- **Expected outcome**: exact descriptor lookup works

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-contention concurrent registrations | Medium | Locking exists, contention profile untested |

### Out-of-Scope Use Cases

- Open-generic handlers with multiple type parameters.
