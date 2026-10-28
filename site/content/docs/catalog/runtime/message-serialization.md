# Message Serialization

- **ID**: `runtime.message-serialization`
- **Name**: Message serialization
- **Maturity**: GA
- **Summary**: The runtime serializes and deserializes message payload text through `IMessageSerializer`, with `SystemTextJsonMessageSerializer` as the default.

## What It Does

Runtime serialization converts CLR message objects to payload strings and back. `MessageModule` registers `IMessageSerializer` to `SystemTextJsonMessageSerializer`, which uses `System.Text.Json` web defaults and wraps JSON conversion failures in `MessageSerializationException`.

## Stating the Wire Format

The default is `JsonSerializerDefaults.Web`, which is camel case. The serializer decides the wire format of every persisted message, so choosing it is a compose-time decision, stated on `MessageModuleBuilder` the same way `UseTimeProvider` is:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

liteBus.AddMessaging(messaging => messaging
    .UseSerializerInstance(new SystemTextJsonMessageSerializer(options)));
```

| Member | Use when |
| --- | --- |
| `UseSerializerInstance(IMessageSerializer)` | The serializer is configured rather than injected, which is the common case |
| `UseSerializer<TMessageSerializer>(InstanceLifetime)` | The serializer has dependencies and should be constructed by the container |

Replacing the registration after `AddLiteBus` still works, but no composition check and no startup check can detect that choice. Forgetting it renames every property of every durable message on the wire, so every consumer breaks. Nothing fails at startup. Applications whose published payloads are read by other systems should state the format here rather than inherit it.

Existing rows are not rewritten. Changing the serializer of a system with messages already in the outbox changes how new rows are written and how every row is read. Drain the backlog before changing it, or keep a serializer that reads both shapes.

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

- The serializer rejects null message or payload values.
- The serializer checks the cancellation token before serialization and deserialization.
- The serializer reports JSON and unsupported-type failures as `MessageSerializationException`.

## Non-Goals

- Binary encodings in the core runtime package.
- Automatic migration of schemas.

## Observability

The runtime core defines no serializer-specific meter or activity contract. Failures surface as `MessageSerializationException`, which carries the message type or contract name, the contract version, and the operation that failed.

## Test Coverage

### Covered Use Cases

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldRegisterCoreServicesAndHostedProcessors`

- **Use case**: runtime composition registers the serializer service in the core graph
- **Test kind**: Component
- **Description**: The composition smoke test validates the required runtime service registrations
- **Behavior**: `MessageModule` contributes core messaging services including `IMessageSerializer`
- **Expected outcome**: the composed host contains the messaging runtime services
- **Remarks**: `LiteBus.Runtime.UnitTests`

#### `PlainMessageTests.Send_FakeCommand_ShouldGoThroughHandlersCorrectly`

- **Use case**: message lifecycle through runtime contracts with serializable models
- **Test kind**: Unit
- **Description**: The test sends a plain command through the message pipeline
- **Behavior**: The pipeline executes with the runtime message contracts and the serializer abstraction in place
- **Expected outcome**: the handler pipeline completes with the expected result
- **Remarks**: `LiteBus.Mediator.UnitTests`

#### `SystemTextJsonMessageSerializerTests`

- **Use case**: Direct serializer behavior covers round trips, custom options, malformed input, unsupported runtime types, JSON null, cancellation, and null arguments.
- **Test kind**: Unit
- **Description**: The test invokes `SystemTextJsonMessageSerializer` without an envelope or mediator wrapper.
- **Behavior**: The serializer maps JSON failures to `MessageSerializationException` with the requested type and operation.
- **Expected outcome**: Valid messages round-trip. Failures on invalid inputs keep their exception context.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Serialization/`

### Out-of-Scope Use Cases

- Alternative serializers such as Protobuf or Avro in the core runtime package.
