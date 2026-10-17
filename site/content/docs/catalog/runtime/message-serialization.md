# Message Serialization

- **ID**: `runtime.message-serialization`
- **Name**: Message serialization
- **Maturity**: GA
- **Summary**: Serializes and deserializes message payload text through `IMessageSerializer`, with default `SystemTextJsonMessageSerializer`.

## What It Does

Runtime serialization converts CLR message objects to payload strings and back. `MessageModule` registers `IMessageSerializer` to `SystemTextJsonMessageSerializer`, which uses `System.Text.Json` web defaults and wraps JSON conversion failures in `MessageSerializationException`.

## Stating the Wire Format

The default is `JsonSerializerDefaults.Web`, which is camel case. The serializer decides the wire format of every persisted message, so it is a compose-time decision like `UseTimeProvider` and is stated on `MessageModuleBuilder`:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

liteBus.AddMessaging(messaging => messaging
    .UseSerializerInstance(new SystemTextJsonMessageSerializer(options)));
```

| Member | Use when |
| --- | --- |
| `UseSerializerInstance(IMessageSerializer)` | The serializer is configured rather than injected, which is the common case |
| `UseSerializer<TMessageSerializer>(InstanceLifetime)` | The serializer has dependencies and should be constructed by the container |

Replacing the registration after `AddLiteBus` still works, but it is a decision no composition check can see and no startup check can miss. Forgetting it renames every property of every durable message on the wire, every consumer breaks, and nothing fails at boot. Applications whose published payloads are read by other systems should state the format here rather than inherit it.

Existing rows are not rewritten. Changing the serializer of a system with messages already in the outbox changes how new rows are written and how every row is read, so drain the backlog before changing it, or keep a serializer that reads both shapes.

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

No serializer-specific meter or activity contract exists in runtime core. Failures surface as `MessageSerializationException`, which carries the message type or contract name, the contract version, and the operation that failed.

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
