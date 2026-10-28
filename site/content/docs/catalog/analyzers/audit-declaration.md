# Audit Declaration

## Header

- **ID**: `analyzers.missing-audit-declaration`
- **Diagnostics**: `LB1018` (Warning, disabled by default)
- **Maturity**: GA
- **Summary**: Reports command and query types that state no audit position, so an unaudited message is a recorded decision rather than an oversight.

## Purpose

Auditing standards require teams to document which events they audit and why. When the choice is implicit in whether a message happens to carry an attribute, no check catches the next message that should have been audited but was not. A missing declaration and a deliberate exemption look identical.

This rule requires a position on every command and query. The rationale for not auditing then lives next to the code it describes rather than in a document that drifts.

`LB1018` is the preconfigured instance of a general rule. It asks the same question [`LB1020`](required-declarations.md) asks, with `AuditDeclaration` as the required value type. To require your own declarations the same way, configure the general rule instead of writing a new analyzer.

## Trigger Conditions

The rule reports a command or query type when every condition below holds:

- The type is declared in the analyzed assembly and is not abstract.
- The type does not carry `[Audited]` or `[AuditExempt]`.
- No `IAuditDefinition<TMessage>` in the assembly describes the type or a base type or interface it implements.
- No `IMessageDefinition<TMessage>` in the assembly states an audit position for the type in its `Describe` body.
- The type carries no `[DeclarationExempt(typeof(AuditDeclaration), "rationale")]`.

When the audit contracts are not referenced at all, the rule stays silent, so a codebase that has not adopted auditing sees no diagnostics.

## Definition Shapes

`IAuditDefinition<TMessage>` names `AuditDeclaration` in its contract, so the interface settles the check. A definition that uses `Describe` names nothing in its contract, so the analyzer reads the body instead. Both `Audited(...)` and `NotAudited(...)` clear the rule, because each states an audit position. The rule asks whether the message answered, not which answer it gave.

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

A definition that exists is not enough on its own. A definition that describes a permission but never states an audit position is still reported, because the two are different declarations.

Three cases cannot be read, so the analyzer treats them as declared: a definition in a referenced assembly, an implementation the analyzer cannot locate, and a body that hands the collector to another method. That resolves in favour of the build, because a false positive here is a build that cannot be made to pass without turning the rule off. Where the rule has to hold with no gaps, `messaging.RequireDeclaration<AuditDeclaration>()` reads the resolved metadata at composition and sees every shape.

## Enabling the Rule

`LB1018` is **disabled by default**, because turning it on silently would break every existing compilation. Once your codebase has declared its position, enable it:

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.LB1018.severity = warning
```

When the audit trail is a compliance obligation rather than a convenience, promote it to `error`.

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

Declare it from a definition beside the message:

```csharp
public sealed class RefundOrderCommandDefinition : IAuditDefinition<RefundOrderCommand>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.refund-order") with
    {
        Category = "money",
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
| `AuditDefinitionOnAMarkerInterface_CoversTheFamily` | `LiteBus.Analyzers.UnitTests` |
| `GeneralDeclarationExemption_SatisfiesTheAuditPosition` | `LiteBus.Analyzers.UnitTests` |
| `CommandDescribedThroughDescribe_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `CommandDescribedAsNotAudited_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeOnAMarkerInterface_CoversTheFamily` | `LiteBus.Analyzers.UnitTests` |
| `DescribeThatDelegatesToAHelper_ProducesNoDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `DescribeThatDeclaresSomethingElse_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |

## References

- [Auditing](../../concepts/auditing.md)
- [Message definitions](../../concepts/message-definitions.md)
- [Required declarations (LB1020)](required-declarations.md)
