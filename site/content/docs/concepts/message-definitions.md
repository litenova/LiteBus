# Message Definitions

A message definition declares metadata about a message: facts that pipeline stages outside the handler need to read, such as whether the message is audited or what permission it requires. LiteBus resolves definitions once, when the message registry is built, and exposes the result on the message descriptor. This page explains what belongs in a definition, how facets keep them segregated, and how definitions relate to attributes.

## Why Not Just Attributes

Attributes work, and LiteBus supports them. They are the right shape when the declaration is a single fact:

```csharp
[AuditExempt("browsing a public storefront is not a sensitive action")]
public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

They stop being the right shape when the declaration wants real types, shared constants, or more than one concern. An attribute argument must be a compile-time constant, so an action code is a magic string and a permission is a magic string, neither checked by the compiler and neither found by a rename.

A definition is ordinary C#. It lives beside the message it describes, so a feature folder holds the command, its handler, its validator, and its definition together:

```
Organizations/CreateOrganization/
    CreateOrganizationCommand.cs
    CreateOrganizationCommandHandler.cs
    CreateOrganizationCommandResult.cs
    CreateOrganizationCommandValidator.cs
    CreateOrganizationCommandDefinition.cs
```

## Facets

A definition does not implement one large interface. It implements one small interface per concern, each keyed by the type of the value it contributes:

```csharp
public sealed class PlaceOrderCommandDefinition :
    IAuditDefinition<PlaceOrderCommand>,
    IPermissionDefinition<PlaceOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order") with
    {
        Category = "money",
        TargetKind = "order"
    };

    public RequiredPermission Required => Permissions.Orders.Place;
}
```

Segregating by facet means a definition that only declares one thing is not forced to implement the others. It also means a class can declare several concerns without them interfering, because each facet closes `IMessageDefinition<TMessage, TValue>` over a different `TValue`.

## Declaring Your Own Facets

`IAuditDefinition<TMessage>` is the only facet LiteBus ships, but the mechanism is open. A facet is any interface deriving from `IMessageDefinition<TMessage, TValue>` that forwards `Value` to a better-named member:

```csharp
public sealed record RequiredPermission(string Name);

public interface IPermissionDefinition<TMessage> : IMessageDefinition<TMessage, RequiredPermission>
    where TMessage : notnull
{
    RequiredPermission Required { get; }

    RequiredPermission IMessageDefinition<TMessage, RequiredPermission>.Value => Required;
}
```

LiteBus discovers and applies this facet without knowing what a permission is. Read it back in a pre-handler:

```csharp
public sealed class AuthorizeCommand : ICommandPreHandler
{
    private readonly IMessageRegistry _registry;
    private readonly IAccessAuthorizer _authorizer;

    public AuthorizeCommand(IMessageRegistry registry, IAccessAuthorizer authorizer)
    {
        _registry = registry;
        _authorizer = authorizer;
    }

    public async Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        var descriptor = _registry.Find(message.GetType());

        if (descriptor?.Metadata.TryGet<RequiredPermission>(out var permission) == true)
        {
            await _authorizer.AuthorizeAsync(permission.Name, cancellationToken);
        }
    }
}
```

This is the division that keeps the framework honest. **What a use case requires** is a declaration, and LiteBus carries it. **Whether the current actor holds it** is enforcement, and that stays in your application, because no messaging library can model your tenancy and role structure.

## What Belongs in a Definition

A useful rule: metadata belongs in a definition only if a pipeline stage **outside the handler** needs to read it before or after the handler runs.

| Belongs | Does not belong |
| --- | --- |
| The audit position of the message | Business rules the handler applies |
| The permission the use case requires | Default values the handler fills in |
| Retry or idempotency policy read by a processor | Anything only the handler reads |

Without that rule, a per-message configuration file becomes a dumping ground and turns into a second, worse handler.

Note that handler-level configuration is a different altitude and stays where it is. `[HandlerPriority]` and `[HandlerTag]` describe one implementation's position in a pipeline, not a fact about the use case. The test that separates them: delete the handler and write a new one, then ask which declarations survive.

## Registration and Precedence

Register a definition like any other construct, or let assembly scanning find it:

```csharp
registry.AddCommands(builder =>
{
    builder.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);
});
```

Two sources populate message metadata, and they have a defined order:

1. **Attributes** on the message type are applied first. Each attribute is stored under its own type, so `descriptor.Metadata.TryGet<AuditedAttribute>(...)` finds it.
2. **Definitions** are applied second, so a definition always wins over an attribute declaring the same value type.

That is the same arrangement LiteBus already uses for durable message contracts, where an attribute and an explicit registration can both express the contract and the analyzer keeps them consistent.

Two constraints are worth knowing:

- A definition must expose a **parameterless constructor**, public or not. Definitions are declarative and are instantiated once during registration, so they cannot take dependencies.
- The **order in which definitions are applied is undefined**. A definition must not depend on another definition having run first.

## Reading Metadata

`IMessageDescriptor.Metadata` exposes everything resolved for a message type:

```csharp
var descriptor = registry.Find(typeof(PlaceOrderCommand));

descriptor.Metadata.TryGet<AuditDeclaration>(out var audit);
descriptor.Metadata.Contains<RequiredPermission>();
```

Because it is resolved once at registration, reading it costs a dictionary lookup rather than reflection on every dispatch.

## Next

See [Auditing](auditing.md) for the facet LiteBus ships and the trail it feeds, and [The Handler Pipeline](handler-pipeline.md) for the stages that read this metadata.
