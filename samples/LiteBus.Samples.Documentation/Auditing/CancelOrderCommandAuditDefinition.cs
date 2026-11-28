// <docs-snippet audit-definition-with-target>
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Samples.Documentation.Auditing;

/// <summary>
///     Declares the audit record written when an order is cancelled.
/// </summary>
public sealed class CancelOrderCommandAuditDefinition : IAuditDefinition<CancelOrderCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit =>
        AuditDeclaration.Audited("orders.cancel-order", category: "money", targetKind: "order")
            .WithTargetFrom<CancelOrderCommand>(command => command.OrderId.ToString());
}
// </docs-snippet>
