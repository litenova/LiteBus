# Audit Declaration

## Header

- **ID**: `analyzers.missing-audit-declaration`
- **Diagnostics**: `LB1018` (Warning, disabled by default)
- **Maturity**: GA
- **Summary**: Reports command and query types that state no audit position, so an unaudited message is a recorded decision rather than an oversight.

## Why It Exists

Auditing standards ask for the selection of audited events to be documented along with its rationale. When the choice is implicit in whether a message happens to carry an attribute, nothing catches the next message that should have been audited and was not. A missing declaration and a deliberate exemption look identical.

This rule makes the choice total. Every command and query states a position, and the rationale for not auditing lives next to the code it describes rather than in a document that drifts.

## When It Reports

Reports for a command or query type when all of the following are true:

- The type is declared in the analyzed assembly and is not abstract.
- The type does not carry `[Audited]` or `[AuditExempt]`.
- No `IAuditDefinition<TMessage>` facet in the assembly describes it.

The rule stays silent when the audit contracts are not referenced at all, so a codebase that has not adopted auditing sees nothing.

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
| `UndeclaredCommand_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |
| `UndeclaredQuery_ProducesDiagnostic` | `LiteBus.Analyzers.UnitTests` |

## Deep Docs

- [Auditing](../../concepts/auditing.md)
- [Message definitions](../../concepts/message-definitions.md)
