# Message Definitions

- **ID**: `mediator.message-definitions`
- **Name**: Message definitions and metadata
- **Maturity**: GA
- **Summary**: Resolves declarative per-message metadata from attributes and definition facets, once at registration, for pipeline stages to read.

## What It Does

A message definition declares facts about a message that stages outside the handler need: whether it is audited, what permission it requires, and anything an application chooses to add. LiteBus resolves them when the message registry is built and exposes the result on `IMessageDescriptor.Metadata`, so a pipeline stage reads a dictionary rather than reflecting on every dispatch.

Two sources populate metadata, in a defined order:

1. Attributes on the message type, each stored under its own attribute type.
2. Definition facets, applied second, so a definition wins over an attribute declaring the same value type.

Facets are segregated by value type. A definition class implements one small interface per concern, so declaring an audit position does not force it to declare a permission. Because a facet closes `IMessageDefinition<TMessage, TValue>` over a distinct `TValue`, several facets coexist on one class without ambiguity, and applications may declare facets over their own value types that LiteBus applies without understanding.

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
| `IMessageDefinition<TMessage, TValue>` | One metadata facet, keyed by `TValue` |
| `IMessageMetadata` | Read side exposed on the message descriptor |
| `IMessageDescriptor.Metadata` | Resolved metadata for one message type |
| `IAuditDefinition<TMessage>` | The audit facet shipped by LiteBus |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `mediator.module-registration`

## Invariants

- A definition type must expose a parameterless constructor, public or non-public.
- A definition type must declare at least one facet, or registration throws `LiteBusConfigurationException`.
- A facet value must not be null.
- Definitions are applied after attributes, so a definition overrides an attribute of the same value type.
- The order in which definitions are applied is undefined; a definition must not depend on another having run first.
- Metadata is resolved once per message type at registration, not per dispatch.
- Facets match the message type exactly; a definition for a base type does not apply to derived message types.

## Non-Goals

- Interpreting application-owned facet values. LiteBus carries the declaration; enforcement stays in the application.
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
| `An_application_owned_facet_is_applied_without_LiteBus_knowing_it` | `LiteBus.Mediator.UnitTests` |
| `Attributes_are_exposed_as_message_metadata` | `LiteBus.Mediator.UnitTests` |

### Untested

- Definitions registered for open generic message types.
- Very large facet counts on a single definition class.

### Out-of-Scope

- Durable message contract registration, covered by `IContractWriter`.

## Deep Docs

- [Message definitions](../../concepts/message-definitions.md)
- [Auditing](../../concepts/auditing.md)
