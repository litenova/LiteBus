# Message Serialization

- **ID**: `runtime.message-serialization`
- **Name**: Message serialization
- **Maturity**: GA
- **Summary**: Serializes and deserializes message payload text through `IMessageSerializer`, with default `SystemTextJsonMessageSerializer`.

## What It Does

Runtime serialization converts CLR message objects to payload strings and back. `MessageModule` registers `IMessageSerializer` to `SystemTextJsonMessageSerializer`, which uses `System.Text.Json` web defaults and wraps JSON conversion failures in `MessageSerializationException`.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageSerializer.SerializeAsync<TMessage>(...)` | Serializes message instance to payload text |
| `IMessageSerializer.DeserializeAsync(Type, string, ...)` | Deserializes payload text to CLR message |
| `SystemTextJsonMessageSerializer(JsonSerializerOptions?)` | Default runtime serializer implementation |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-module`
- `runtime.contract-registry`

## Invariants

- Null message or payload values are rejected.
- Cancellation token is checked before serialization and deserialization.
- JSON and unsupported-type failures are surfaced as `MessageSerializationException`.

## Non-Goals

- Binary encodings in core runtime package.
- Automatic schema migration support.

## Observability

No serializer-specific meter or activity contract exists in runtime core. Call sites log serialization exceptions with contract context.

## Test Coverage

### Covered Use Cases

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldRegisterCoreServicesAndHostedProcessors`

- **Use case**: runtime composition registers serializer service in core graph
- **Test kind**: Component
- **Description**: Composition smoke validates required runtime service registrations
- **Behavior**: `MessageModule` contributes core messaging services including `IMessageSerializer`
- **Expected outcome**: composed host contains messaging runtime services
- **Remarks**: `LiteBus.Runtime.UnitTests`

#### `PlainMessageTests.Send_FakeCommand_ShouldGoThroughHandlersCorrectly`

- **Use case**: message lifecycle through runtime contracts with serializable models
- **Test kind**: Unit
- **Description**: sends plain command through message pipeline
- **Behavior**: pipeline executes with runtime message contracts and serializer abstraction in place
- **Expected outcome**: handler pipeline completes with expected result
- **Remarks**: `LiteBus.Mediator.UnitTests`

#### `SystemTextJsonMessageSerializerTests`

- **Use case**: Direct serializer behavior covers round trips, custom options, malformed input, unsupported runtime types, JSON null, cancellation, and null arguments.
- **Test kind**: Unit
- **Description**: Invokes `SystemTextJsonMessageSerializer` without an envelope or mediator wrapper.
- **Behavior**: JSON failures are mapped to `MessageSerializationException` with the requested type and operation.
- **Expected outcome**: Valid messages round-trip; invalid inputs retain exception context.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Serialization/`.

### Out-of-Scope Use Cases

- Alternative serializers such as Protobuf or Avro in core runtime package.
