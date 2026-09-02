# Required Declarations

## Header

- **ID**: `analyzers.missing-declaration`
- **Diagnostics**: `LB1020` (Warning, disabled by default), `LB1021` (Warning, enabled by default)
- **Maturity**: GA
- **Summary**: Reports command and query types that state no position on a metadata value type the project requires them to declare.

## Why It Exists

Auditing was the first cross-cutting declaration worth enforcing at compile time, and `LB1018` enforces it. It is not the only one. A required permission, a tenancy scope, a retention class and an idempotency key are all facts a message either states or silently omits, and the omission is the failure that matters: an unguarded use case looks exactly like one that needs no guard.

Without this rule, an application that wants the same guarantee for its own declarations writes a reflection test over its assemblies and re-derives what `LB1018` already does. This is the general form, and `LB1018` is now the preconfigured instance of it for `AuditDeclaration`.

## Configuring It

Name the metadata value types in `.editorconfig` and enable the rule. Both a `.editorconfig` entry and a `.globalconfig` entry are read.

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = Entro.Security.RequiredPermission, Entro.Compliance.RetentionClass
dotnet_diagnostic.LB1020.severity = warning
```

Use the full metadata name of each value type. Separate several with commas or semicolons. With nothing configured the rule reports nothing, so referencing the analyzer package changes no existing build.

`LB1021` reports a configured name that does not resolve in the compilation. A name that silently did nothing would disable the requirement it configures, and a typo in `.editorconfig` is exactly how that happens.

## When It Reports

`LB1020` reports for a command, query, or stream query type when all of the following are true:

- The type is declared in the analyzed assembly and is not abstract.
- No definition class declares the required value type for it, or for a base type or interface it implements.
- The type carries no attribute annotated `[MessageDeclaration(typeof(TValue))]` for the required value type.
- The type carries no `[DeclarationExempt(typeof(TValue), "rationale")]` for the required value type.

## Making an Attribute Analyzable

A definition class needs nothing: its declaration is already in the type system, as the second type argument of `IMessageDefinition<TMessage, TValue>`.

An attribute needs `[MessageDeclaration]`, because `IMessageDeclarationSource.DeclarationType` is a runtime property an analyzer cannot execute:

```csharp
[MessageDeclaration(typeof(RequiredPermission))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class RequiresPermissionAttribute : Attribute, IMessageDeclarationSource
{
    public RequiresPermissionAttribute(string permission) => Permission = permission;

    public string Permission { get; }

    public Type DeclarationType => typeof(RequiredPermission);

    public object CreateDeclaration() => new RequiredPermission(Permission);
}
```

Registration verifies that the annotation and the property name the same type, and fails composition when they disagree. The annotation is what the analyzer reads and the property is what the registry reads, so they cannot be allowed to drift.

## Bad Example

```csharp
using LiteBus.Commands.Abstractions;

// LB1020: declares no RequiredPermission
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

## Good Examples

Declare it with the annotated attribute:

```csharp
[RequiresPermission("orders.refund")]
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

Declare it from a definition beside the message:

```csharp
public sealed class RefundOrderCommandDefinition
    : IMessageDefinition<RefundOrderCommand, RequiredPermission>
{
    public RequiredPermission Value => Permissions.Orders.Refund;
}
```

Declare a whole family once, through a marker interface:

```csharp
public sealed class PublicCommandDefinition : IMessageDefinition<IPublicCommand, RequiredPermission>
{
    public RequiredPermission Value => Permissions.Public;
}
```

Or record why the message needs none:

```csharp
[DeclarationExempt(typeof(RequiredPermission), "the storefront is public, so there is no actor to authorize")]
public sealed record BrowseStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

The rationale is the point of the exemption. A message with no declaration is indistinguishable from one nobody considered, which is what the rule exists to prevent, so an exemption without a reason would defeat it.

## The Composition-Time Counterpart

`RequireDeclaration<TValue>()` on the messaging module enforces the same rule when the host is composed:

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<RequiredPermission>()
    .RequireDeclaration<RetentionClass>());
```

It fails with a `LiteBusConfigurationException` naming every offending message, grouped by the declaration each one omits. Every offender is listed rather than the first, because a requirement turned on for an existing codebase reports many at once.

Keep both. The analyzer reports the omission where it can be fixed, message by message, while writing the code. The composition check covers a message registered from an assembly the analyzer never saw, and it holds even where the analyzer package is not referenced.

## Packages

- `LiteBus.Analyzers`
- `LiteBus.Messaging` for `RequireDeclaration<TValue>()`

## Test Coverage

| Test method | Project |
| --- | --- |
| `WithoutConfiguration_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `AttributeDeclaredCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DefinitionDeclaredCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DeclarationOnAMarkerInterface_CoversTheFamily` | `LiteBus.Analyzers.UnitTests` |
| `ExemptCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `ExemptionForAnotherValue_StillProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UnresolvableRequiredType_ProducesConfigurationDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `An_undeclared_message_fails_composition_naming_the_message_and_the_value` | `LiteBus.Mediator.UnitTests` |
| `Every_offender_is_named_rather_than_the_first` | `LiteBus.Mediator.UnitTests` |
| `A_recorded_exemption_satisfies_the_requirement` | `LiteBus.Mediator.UnitTests` |
| `Several_exemptions_on_one_message_are_aggregated` | `LiteBus.Mediator.UnitTests` |

## Deep Docs

- [Message definitions](../../concepts/message-definitions.md)
- [Audit declaration (LB1018)](audit-declaration.md)
