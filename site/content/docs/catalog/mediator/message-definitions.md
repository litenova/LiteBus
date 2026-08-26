# Message Definitions

- **ID**: `mediator.message-definitions`
- **Name**: Message definitions and metadata
- **Maturity**: GA
- **Summary**: Resolves declarative per-message metadata from declaring attributes and definitions, once at registration, for pipeline stages to read.

## What It Does

A message definition declares facts about a message that stages outside the handler need: whether it is audited, what permission it requires, and anything an application chooses to add. LiteBus resolves them when the message registry is built and exposes the result on `IMessageDescriptor.Metadata`, so a pipeline stage reads a dictionary rather than reflecting on every dispatch.

Two sources populate metadata, in a defined order, and both contribute values of the same type:

1. Attributes on the message type that implement `IMessageDeclarationSource`, each converted to the value type it names. Attributes that do not implement it are not metadata and are never collected, which keeps the collection bounded.
2. Definitions, applied second, so a definition wins over an attribute declaring the same value type.

Declarations are keyed by value type. A definition class implements one small interface per concern, so declaring an audit position does not force it to declare a permission. Because each closes `IMessageDefinition<TMessage, TValue>` over a distinct `TValue`, several coexist on one class without ambiguity, and applications may declare their own value types that LiteBus applies without understanding.

A declaration covers the message type it names and every message assignable to it, so a definition over a base type or marker interface describes a family of messages. The most derived declaration wins.

## Public Surface

```csharp
public sealed record RequiredPermission(string Name);

public interface IPermissionDefinition<TMessage> : IMessageDefinition<TMessage, RequiredPermission>
    where TMessage : notnull
{
    RequiredPermission Required { get; }

    RequiredPermission IMessageDefinition<TMessage, RequiredPermission>.Value => Required;
}

public sealed class PlaceOrderCommandDefinition :
    IAuditDefinition<PlaceOrderCommand>,
    IPermissionDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order");
    public RequiredPermission Required => new("orders.place");
}
```

| API | Role |
| --- | --- |
| `IMessageDefinition` | Non-generic marker used for discovery |
| `IMessageDefinition<TMessage, TValue>` | One declaration, keyed by `TValue` |
| `IMessageDeclarationSource` | Implemented by an attribute that declares metadata |
| `IMessageMetadata` | Read side exposed on the message descriptor |
| `IMessageDescriptor.Metadata` | Resolved metadata for one message type |
| `IAuditDefinition<TMessage>` | The declaration shipped by LiteBus |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `mediator.module-registration`

## Invariants

- A definition type must expose a parameterless constructor, public or non-public.
- A definition type must declare at least one value, or registration throws `LiteBusConfigurationException`.
- A declared value must not be null, and must be assignable to the value type it is keyed under.
- Only attributes implementing `IMessageDeclarationSource` become metadata.
- Definitions are applied after attributes, so a definition overrides an attribute of the same value type.
- A declaration covers every message assignable to the type it names; the most derived declaration wins.
- Two definitions declaring the same value type for one message throw `LiteBusConfigurationException` at registration.
- Two declarations covering one message where neither type is more derived than the other throw `LiteBusConfigurationException`.
- Metadata is resolved once per message type at registration, not per dispatch.
- Open generic message shapes are matched exactly, because assignability between generic type definitions is not meaningful.

## Non-Goals

- Interpreting application-owned declared values. LiteBus carries the declaration; enforcement stays in the application.
- Replacing handler-level configuration such as `[HandlerPriority]` and `[HandlerTag]`, which describe an implementation rather than a use case.
- Runtime mutation of metadata after the registry is built.

## Observability

No definition-specific meter or activity source. Binding failures surface as `LiteBusConfigurationException` during module build, so a malformed definition fails at startup rather than at dispatch.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `A_definition_declared_command_produces_a_record` | `LiteBus.Mediator.UnitTests` |
| `A_definition_takes_precedence_over_an_attribute` | `LiteBus.Mediator.UnitTests` |
| `An_application_owned_declaration_is_applied_without_LiteBus_knowing_it` | `LiteBus.Mediator.UnitTests` |
| `An_attribute_is_normalized_to_the_declaration_a_definition_would_contribute` | `LiteBus.Mediator.UnitTests` |
| `Attributes_that_do_not_declare_metadata_are_not_collected` | `LiteBus.Mediator.UnitTests` |
| `A_declaration_over_a_marker_interface_covers_the_messages_beneath_it` | `LiteBus.Mediator.UnitTests` |
| `Two_definitions_declaring_the_same_value_for_one_message_are_reported_at_registration` | `LiteBus.Mediator.UnitTests` |

### Untested

- Definitions registered for open generic message types.
- Two declarations covering one message through unrelated marker interfaces.
- Very large declaration counts on a single definition class.

### Out-of-Scope

- Durable message contract registration, covered by `IContractWriter`.

## Deep Docs

- [Message definitions](../../concepts/message-definitions.md)
- [Auditing](../../concepts/auditing.md)
