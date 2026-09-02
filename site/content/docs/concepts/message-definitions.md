# Message Definitions

A message definition declares metadata about a message: facts that pipeline stages outside the handler need to read, such as whether the message is audited or what permission it requires. LiteBus resolves definitions once, when the message registry is built, and exposes the result on the message descriptor. This page explains what belongs in a definition, how one class declares several things without them interfering, and how definitions relate to attributes.

Three words appear throughout and mean different things. A **declaration** is the value: `AuditDeclaration`, `RequiredPermission`. A **definition** is the class that declares it. **Metadata** is the resolved collection on the message descriptor.

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

## One Interface Per Declaration

A definition does not implement one large interface. It implements one small interface per declaration, each keyed by the type of the value it contributes:

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

Keying by value type means a definition that only declares one thing is not forced to implement the others. It also means a class can declare several concerns without them interfering, because each closes `IMessageDefinition<TMessage, TValue>` over a different `TValue`.

## Declaring Your Own

`IAuditDefinition<TMessage>` is the only declaration LiteBus ships, but the mechanism is open. Any interface deriving from `IMessageDefinition<TMessage, TValue>` that forwards `Value` to a better-named member works:

```csharp
public sealed record RequiredPermission(string Name);

public interface IPermissionDefinition<TMessage> : IMessageDefinition<TMessage, RequiredPermission>
    where TMessage : notnull
{
    RequiredPermission Required { get; }

    RequiredPermission IMessageDefinition<TMessage, RequiredPermission>.Value => Required;
}
```

LiteBus discovers and applies this without knowing what a permission is. Read it back with `IMessageMetadataAccessor`, which is covered in full under [Reading Metadata](#reading-metadata):

```csharp
public sealed class PermissionGuard<TCommand> : ICommandGuard<TCommand>
    where TCommand : ICommand
{
    private readonly IMessageMetadataAccessor _metadata;
    private readonly IAccessAuthorizer _authorizer;

    public PermissionGuard(IMessageMetadataAccessor metadata, IAccessAuthorizer authorizer)
    {
        _metadata = metadata;
        _authorizer = authorizer;
    }

    public async Task<Verdict> DecideAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        if (!_metadata.TryGet<TCommand, RequiredPermission>(out var permission))
        {
            return Verdict.Allow;
        }

        return await _authorizer.HoldsAsync(permission.Name, cancellationToken)
            ? Verdict.Allow
            : Verdict.Deny($"the caller does not hold {permission.Name}");
    }
}
```

Register it once as an open generic and it covers every command:

```csharp
registry.AddCommands(builder => builder.Register(typeof(PermissionGuard<>)));
```

That is the whole payoff of the declaration model. One guard replaces one authorization call per handler, and a message that forgets to declare its permission is a build failure rather than an unguarded use case, once you add [Requiring a Declaration](#requiring-a-declaration).

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

Two sources populate message metadata, and both contribute values of the same type. An attribute is metadata only if it implements `IMessageDeclarationSource`, which states the value type it declares and converts itself to it:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class RequiresPermissionAttribute : Attribute, IMessageDeclarationSource
{
    public RequiresPermissionAttribute(string permission) => Permission = permission;

    public string Permission { get; }

    public Type DeclarationType => typeof(RequiredPermission);

    public object CreateDeclaration() => new RequiredPermission(Permission);
}
```

Requiring that is what keeps metadata bounded. A message type carries attributes for serialization, diagnostics and source generators, and collecting all of them would make `Metadata.Contains<T>()` answer questions LiteBus never meant to answer. It also puts both sources on one key, so the precedence rule is real rather than nominal:

1. **Attributes** that declare metadata are applied first, each converted to the value type it names.
2. **Definitions** are applied second, so a definition always wins over an attribute declaring the same value type.

That is the same arrangement LiteBus already uses for durable message contracts, where an attribute and an explicit registration can both express the contract and the analyzer keeps them consistent.

### Inheritance and Conflicts

A declaration applies to the message type it names **and to every message assignable to it**, so one definition can describe a family of messages through a base type or a marker interface. When two declarations of the same value type both cover a message, the more derived one wins, which matches how attributes are inherited and how the registry resolves indirect handlers.

Two situations are configuration errors and are reported at registration rather than resolved by scanning order:

- Two definitions declaring the same value type for the same message. Letting the last one win would make the effective configuration depend on file layout.
- Two declarations covering a message where neither type is more derived than the other, such as two unrelated marker interfaces. Declare the value for the message itself to say which one applies.

Open generic message shapes are matched exactly, because assignability between generic type definitions is not meaningful.

One more constraint is worth knowing: a definition must expose a **parameterless constructor**, public or not. Definitions are declarative and are instantiated once during registration, so they cannot take dependencies.

## Requiring a Declaration

A declaration is only as good as its coverage. One command that forgets to declare the permission it requires is an unguarded use case, and it looks exactly like a command that needs no permission. Two mechanisms close that gap, and they cover different ground.

At composition time, on the messaging module:

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<RequiredPermission>()
    .RequireDeclaration<RetentionClass>());
```

Every registered message must then declare the value or record an exemption from it. The check runs once every module has built, because the messaging module is foundational and has no commands to inspect while it is being built. A failure is a `LiteBusConfigurationException` naming every offender, grouped by the declaration each one omits:

```text
One or more registered messages state no position on a required declaration:
  RequiredPermission is not declared by: DraftScheduleCommand, WithdrawScheduleCommand
Declare the value with an attribute or a definition class, or record why the message does not need it
with [DeclarationExempt(typeof(TValue), "rationale")].
```

At compile time, with `LB1020`:

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = App.Security.RequiredPermission
dotnet_diagnostic.LB1020.severity = warning
```

Use both. The analyzer reports the omission on the message itself while you are writing it; the composition check covers a message registered from an assembly the analyzer never saw. See [Required Declarations](../catalog/analyzers/required-declarations.md) for the full rule, including how to make your own attribute analyzable with `[MessageDeclaration]`.

### Recording an Exemption

A message that genuinely needs no declaration says so, with a reason:

```csharp
[DeclarationExempt(typeof(RequiredPermission), "the storefront is public, so there is no actor to authorize")]
public sealed record BrowseStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

The rationale is the whole point. An exemption is a decision and an omission is an accident, and without the reason written down there is nothing to tell them apart, which is the situation the requirement exists to end.

The attribute may be applied more than once and every instance is aggregated into a single `DeclarationExemptions` metadata value, readable through the accessor like any other declaration:

```csharp
accessor.TryGet<BrowseStorefrontQuery, DeclarationExemptions>(out var exemptions);
exemptions.TryGet(typeof(RequiredPermission), out var exemption);
// exemption.Rationale
```

Auditing is the exception that needs no exemption attribute. `[AuditExempt]` already produces an `AuditDeclaration`, because the audit position models both answers in one value type, so an audit-exempt message satisfies `RequireDeclaration<AuditDeclaration>()` on its own. Most application value types cannot model an absence that way, which is why the general exemption exists.

## Declaring a Value Computed From the Message

A declaration is resolved once at registration and cannot take dependencies, which is often read as "constants only". It is not. The value is an ordinary object, so it can carry a delegate over the message, and that is the difference between a generic handler covering only the constant cases and one covering everything derivable from the message itself.

An authorization scope is the common case. Some commands need a fixed permission; others need one scoped to the organization named in the command:

```csharp
public sealed record AuthorizationScope(Func<object, string> Resolve)
{
    public static AuthorizationScope Fixed(string scope) => new(_ => scope);

    public static AuthorizationScope FromMessage<TMessage>(Func<TMessage, string> resolve)
        where TMessage : notnull
        => new(message => resolve((TMessage) message));
}

public sealed class ArchiveOccurrenceCommandDefinition
    : IMessageDefinition<ArchiveOccurrenceCommand, AuthorizationScope>
{
    public AuthorizationScope Value =>
        AuthorizationScope.FromMessage<ArchiveOccurrenceCommand>(command => command.OrganizationId);
}
```

The guard then resolves the scope per message from a declaration it reads once:

```csharp
if (_metadata.TryGet<TCommand, AuthorizationScope>(out var scope))
{
    var resolved = scope.Resolve(message);
    // ...
}
```

The constraint that remains is real: the delegate is built at registration and cannot resolve services, so it can only project from the message. Anything needing a database read belongs in the guard, which can inject what it needs and hand the result forward through [`IExecutionContext.Data`](execution-context.md).

## Reading Metadata

Resolve `IMessageMetadataAccessor` from the container. It is the supported way to read declarations from application code:

```csharp
public interface IMessageMetadataAccessor
{
    IMessageMetadata ForMessage(Type messageType);
    IMessageMetadata ForMessage<TMessage>() where TMessage : notnull;
    bool TryGet<TValue>(Type messageType, out TValue value) where TValue : notnull;
    bool TryGet<TMessage, TValue>(out TValue value) where TMessage : notnull where TValue : notnull;
}
```

```csharp
accessor.TryGet<PlaceOrderCommand, RequiredPermission>(out var permission);
accessor.ForMessage(message.GetType()).Contains<AuditDeclaration>();
```

Because metadata is resolved once at registration, reading it costs a dictionary lookup rather than reflection on every dispatch, and the accessor is a stateless view with nothing to invalidate.

A type the registry does not hold raises `MessageMetadataNotFoundException` rather than answering with an empty collection. That is deliberate: an empty answer would turn a missing registration into a permission check that silently passes.

`IMessageDescriptor.Metadata` is still there and still public, but reaching for it from application code means the registry's descriptor shape becomes part of your application. Use the accessor.

## Next

See [Auditing](auditing.md) for the declaration LiteBus ships and the trail it feeds, and [The Handler Pipeline](handler-pipeline.md) for the stages that read this metadata.
