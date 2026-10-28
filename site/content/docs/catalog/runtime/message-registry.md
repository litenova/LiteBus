# Message and Handler Type Registry

- **ID**: `runtime.message-registry`
- **Name**: Message and handler type registry
- **Maturity**: GA
- **Summary**: The registry registers message and handler CLR types, builds descriptors, and supports open-generic handler expansion.

## What It Does

`MessageRegistry` implements `IMessageRegistry` (`IMessageWriter` and `IMessageReader`). It provides exact message descriptor lookup and ordered handler descriptors, and holds open-generic handler templates that are closed over concrete messages.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageWriter.Register(Type)` | Registers message or handler type |
| `IMessageWriter.RegisterFromScan(Type)` | Registers a scan-discovered type and records that no registration line named it |
| `IMessageReader.OpenGenericClosures` | Each open generic handler and the concrete messages it closed over |
| `IMessageReader.ScannedOpenGenericHandlers` | Open generic handlers that arrived through a scan rather than a registration line |
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
- The registry normalizes generic message types for lookup.
- Registering an unsupported open-generic handler shape throws.

## Non-Goals

- Storing the registry in persistent storage.

## Observability

The registry emits no telemetry.

## Test Coverage

### Covered Use Cases

#### `MessageRegistryTests.Register_OpenGenericHandler_ShouldLinkToExistingConcreteMessageType`
- **Test kind**: Unit
- **Expected outcome**: the registry closes and links the open generic handler

#### `MessageRegistryTests.Register_ConcreteMessageAfterOpenGenericHandler_ShouldLinkOpenGenericHandler`
- **Test kind**: Unit
- **Expected outcome**: a later concrete message receives the closed handler

#### `MessageRegistryTests.Find_ExactType_ShouldReturnDescriptorInConstantTime`
- **Test kind**: Unit
- **Expected outcome**: exact descriptor lookup returns the descriptor

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| High-contention concurrent registrations | Medium | Locking exists but the contention profile is untested |

### Out-of-Scope Use Cases

- Supporting open-generic handlers with more than two type parameters.
