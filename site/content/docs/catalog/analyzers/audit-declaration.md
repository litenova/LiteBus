# Audit Declaration

## Header

- **ID**: `analyzers.missing-audit-declaration`
- **Diagnostics**: `LB1018` (Warning, disabled by default)
- **Maturity**: GA
- **Summary**: Reports command and query types that state no audit position, so an unaudited message is a recorded decision rather than an oversight.

## Why It Exists

Auditing standards ask for the selection of audited events to be documented along with its rationale. When the choice is implicit in whether a message happens to carry an attribute, nothing catches the next message that should have been audited and was not. A missing declaration and a deliberate exemption look identical.

This rule makes the choice total. Every command and query states a position, and the rationale for not auditing lives next to the code it describes rather than in a document that drifts.

`LB1018` is the preconfigured instance of a general rule. It asks the same question [`LB1020`](required-declarations.md) asks, with `AuditDeclaration` as the required value type. Requiring your own declarations the same way is a matter of configuration rather than a new analyzer.

## When It Reports

Reports for a command or query type when all of the following are true:

- The type is declared in the analyzed assembly and is not abstract.
- The type does not carry `[Audited]` or `[AuditExempt]`.
- No `IAuditDefinition<TMessage>` in the assembly describes it, or describes a base type or interface it implements.
- No `IMessageDefinition<TMessage>` in the assembly states an audit position for it in its `Describe` body.
- The type carries no `[DeclarationExempt(typeof(AuditDeclaration), "rationale")]`.

The rule stays silent when the audit contracts are not referenced at all, so a codebase that has not adopted auditing sees nothing.

## Both Definition Shapes Count

`IAuditDefinition<TMessage>` names `AuditDeclaration` in its contract, so the interface settles it. A definition using `Describe` names nothing in its contract, so the body is read instead, and both `Audited(...)` and `NotAudited(...)` clear the rule: each states an audit position, and the rule asks whether the message answered rather than which answer it gave.

```csharp
public sealed class ShipOrderCommandDefinition : IMessageDefinition<ShipOrderCommand>
{
    public void Describe(IMessageDeclarations declarations)
    {
        // Clears LB1018, exactly as [Audited] or an IAuditDefinition would.
        declarations.Audited("orders.ship-order", category: "lifecycle");
    }
}
```

A definition that exists is not on its own enough: one that describes a permission but never states an audit position is still reported, because the two are different declarations.

Three cases cannot be read and are treated as declared: a definition in a referenced assembly, an implementation the analyzer cannot locate, and a body that hands the collector to another method. That resolves in favour of the build, because a false positive here is a build that cannot be made to pass without turning the rule off. Where the rule has to hold with no gaps, `messaging.RequireDeclaration<AuditDeclaration>()` reads the resolved metadata at composition and sees every shape.

## Enabling It

`LB1018` is **disabled by default**, because turning it on silently would break every existing compilation. Enable it once your codebase has declared its position:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1018.severity = warning
```

Promote it to `error` when the trail is a compliance obligation rather than a convenience.

## Bad Example

```csharp
using LiteBus.Commands.Abstractions;

// LB1018: states no audit position
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

## Good Examples

Declare the message audited:

```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

[Audited("orders.refund-order", Category = "money", TargetKind = "order")]
public sealed record RefundOrderCommand(Guid OrderId) : ICommand;
```

Declare it exempt, with the reason:

```csharp
[AuditExempt("read-only health probe touches no customer data")]
public sealed record PingCommand : ICommand;
```

Or declare it from a definition beside the message:

```csharp
public sealed class RefundOrderCommandDefinition : IAuditDefinition<RefundOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited(AuditActions.Orders.Refund) with
    {
        Category = AuditCategories.Money,
        TargetKind = "order"
    };
}
```

## Packages

- `LiteBus.Analyzers`

## Test Coverage

| Test method | Project |
| --- | --- |
| `AuditedCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `ExemptCommand_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `CommandWithAuditDefinition_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `CommandDescribedThroughDescribe_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `CommandDescribedAsNotAudited_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeOnAMarkerInterface_CoversTheFamily` | `LiteBus.Analyzers.UnitTests` |
| `DescribeThatDelegatesToAHelper_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeThatDeclaresSomethingElse_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |

## Deep Docs

- [Auditing](../../concepts/auditing.md)
- [Message definitions](../../concepts/message-definitions.md)
- [Required declarations (LB1020)](required-declarations.md)
