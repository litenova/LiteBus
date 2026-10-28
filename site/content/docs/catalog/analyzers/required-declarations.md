# Required Declarations

## Header

- **ID**: `analyzers.missing-declaration`
- **Diagnostics**: `LB1020` (Warning, disabled by default), `LB1021` (Warning, enabled by default)
- **Maturity**: GA
- **Summary**: Reports command and query types that state no position on a metadata value type the project requires them to declare.

## Purpose

Auditing was the first cross-cutting declaration worth enforcing at compile time, and `LB1018` enforces it. It is not the only one. A required permission, a tenancy scope, a retention class, and an idempotency key are all facts a message either states or silently omits. The omission is the failure that matters, because an unguarded use case looks exactly like one that needs no guard.

Without this rule, an application that wants the same guarantee for its own declarations writes a reflection test over its assemblies and re-derives what `LB1018` already does. This rule is the general form, and `LB1018` is the preconfigured instance of it for `AuditDeclaration`.

## Configuration

Name the metadata value types in `.editorconfig` and enable the rule. The analyzer reads both `.editorconfig` and `.globalconfig` entries.

```ini
# .editorconfig
[*.cs]
litebus_required_declarations = Entro.Security.RequiredPermission, Entro.Compliance.RetentionClass
dotnet_diagnostic.LB1020.severity = warning
```

Use the full metadata name of each value type. Separate several with commas or semicolons. When nothing is configured the rule reports nothing, so referencing the analyzer package changes no existing build.

`LB1021` reports a configured name that does not resolve in the compilation. A name that silently did nothing would disable the requirement it configures, and a typo in `.editorconfig` is exactly how that happens.

## Trigger Conditions

`LB1020` reports a command, query, or stream query type when every condition below holds:

- The type is declared in the analyzed assembly and is not abstract.
- No definition class declares the required value type for the type or for a base type or interface it implements, in either definition shape.
- The type carries no attribute annotated `[MessageDeclaration(typeof(TValue))]` for the required value type.
- The type carries no `[DeclarationExempt(typeof(TValue), "rationale")]` for the required value type.

## The Two Definition Shapes

`IMessageDefinition<TMessage, TValue>` names its value type in the contract, so the interface settles the check.

`IMessageDefinition<TMessage>.Describe(IMessageDeclarations)` names nothing in the contract, so the analyzer reads the body instead. It matches `Declare<TValue>` and `Exempt<TValue>` on the type argument. It counts `Audited(...)` and `NotAudited(...)` as declaring `AuditDeclaration`, because both state an audit position. The rule asks whether the message answered rather than which answer it gave:

```csharp
using LiteBus.Messaging.Abstractions;

public sealed class ShipOrderCommandDefinition : IMessageDefinition<ShipOrderCommand>
{
    public void Describe(IMessageDeclarations declarations)
    {
        declarations
            .Audited("orders.ship-order", category: "lifecycle")
            .Declare(new RequiredPermission("orders.ship"));
    }
}
```

Three cases cannot be read, so the analyzer treats them as declared: a definition in a referenced assembly, an implementation the analyzer cannot locate, and a body that hands the collector to another method. That last case looks like this:

```csharp
public void Describe(IMessageDeclarations declarations)
{
    // Out of the analyzer's sight, so no diagnostic is reported for this message.
    BillingConventions.Apply(declarations);
}
```

That resolves in favour of the build deliberately. A false negative still fails at startup under `RequireDeclaration`, while a false positive is a build that cannot be made to pass without turning the rule off. To enforce the rule with no gaps at all, call `messaging.RequireDeclaration<TValue>()`, which reads the resolved metadata and sees every shape, including the ones the analyzer cannot.

## Make an Attribute Analyzable

A definition class needs nothing. A keyed definition puts its declaration in the type system as the second type argument of `IMessageDefinition<TMessage, TValue>`, and the analyzer reads a describing definition from its `Describe` body.

An attribute needs `[MessageDeclaration]`, because `IMessageDeclarationSource.DeclarationType` is a runtime property the analyzer cannot execute:

```csharp
using System;
using LiteBus.Messaging.Abstractions;

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
using System;
using LiteBus.Commands.Abstractions;

// LB1020: declares no RequiredPermission
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

## Good Examples

Declare it with the annotated attribute:

```csharp
using System;
using LiteBus.Commands.Abstractions;

[RequiresPermission("orders.refund")]
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

Declare it from a definition beside the message:

```csharp
using LiteBus.Messaging.Abstractions;

public sealed class RefundOrderCommandDefinition
    : IMessageDefinition<RefundOrderCommand, RequiredPermission>
{
    public RequiredPermission Value => Permissions.Orders.Refund;
}
```

Declare a whole family once, through a marker interface:

```csharp
using LiteBus.Messaging.Abstractions;

public sealed class PublicCommandDefinition : IMessageDefinition<IPublicCommand, RequiredPermission>
{
    public RequiredPermission Value => Permissions.Public;
}
```

Record why the message needs none:

```csharp
using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

[DeclarationExempt(typeof(RequiredPermission), "the storefront is public, so there is no actor to authorize")]
public sealed record BrowseStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

The rationale is the point of the exemption. A message with no declaration is indistinguishable from one nobody considered, which is what the rule exists to prevent. An exemption without a reason would therefore defeat it.

## The Composition-Time Counterpart

`RequireDeclaration` on the messaging module enforces the same rule at composition, scoped to the messages the rule covers:

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<RequiredPermission, ICommand>()
    .RequireDeclaration<RequiredPermission, IActingAccountCommand>()
    .RequireDeclaration<RetentionClass>());
```

It fails with a `MessageDeclarationException` that names every offending message, grouped by the declaration each one omits and by the scope of the requirement it violated. Every offender is listed rather than the first, because a requirement turned on for an existing codebase reports many at once.

Three scopes exist. `RequireDeclaration<TValue>()` covers every registered message, `RequireDeclaration<TValue, TScope>()` covers every message assignable to a marker, and `RequireDeclaration<TValue>(predicate, description)` covers an arbitrary selection, described in the words the error uses. Prefer a marker. An unscoped permission requirement also demands one from every query, and the exemptions written to satisfy it say nothing.

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
| `DescribeDeclaredCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeExemptedCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DeclarationOnAMarkerInterface_CoversTheFamily` | `LiteBus.Analyzers.UnitTests` |
| `ExemptCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `ExemptionForAnotherValue_StillProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UnresolvableRequiredType_ProducesConfigurationDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `AuthorizedAttribute_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `AuthorizationExemptAttribute_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `KeyedAuthorizationDefinition_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeAuthorized_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeAuthorizationExempt_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeThatDeclaresSomethingElse_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `An_undeclared_message_fails_composition_naming_the_message_and_the_value` | `LiteBus.Mediator.UnitTests` |
| `Every_offender_is_named_rather_than_the_first` | `LiteBus.Mediator.UnitTests` |
| `A_recorded_exemption_satisfies_the_requirement` | `LiteBus.Mediator.UnitTests` |
| `Several_exemptions_on_one_message_are_aggregated` | `LiteBus.Mediator.UnitTests` |

## References

- [Message definitions](../../concepts/message-definitions.md)
- [Audit declaration (LB1018)](audit-declaration.md)
