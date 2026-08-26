using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Collects the parts of an audit record that only the handler knows.
/// </summary>
/// <remarks>
///     <para>
///         An audit declaration carries the constant half of a record: the action, the category, the kind of resource
///         acted on. The variable half is known only while the handler runs. A command that creates a resource generates
///         its identifier internally, and a reason composed at runtime cannot be declared in advance.
///     </para>
///     <para>
///         Resolve the scope from the container inside a handler and push what you alone know. Everything else comes
///         from the declaration, the caller, and the clock.
///     </para>
///     <para>
///         The scope belongs to the mediation in flight. Calling one of its methods outside a mediation raises
///         <see cref="NoExecutionContextException" />, because there is no record for the value to reach.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, OrderId>
/// {
///     private readonly IAuditScope _audit;
///
///     public PlaceOrderCommandHandler(IAuditScope audit) => _audit = audit;
///
///     public async Task<OrderId> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
///     {
///         var order = Order.Place(message.CartId);
///         _audit.WithTarget(order.Id.ToString());
///         return order.Id;
///     }
/// }
/// ]]></code>
/// </example>
public interface IAuditScope
{
    /// <summary>
    ///     Gets the identifier of the resource the message acted on, when the handler supplied one.
    /// </summary>
    string? TargetId { get; }

    /// <summary>
    ///     Gets the reason the action was taken, when the handler supplied one.
    /// </summary>
    string? Reason { get; }

    /// <summary>
    ///     Gets the additional properties the handler attached to the record.
    /// </summary>
    IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>
    ///     Records the identifier of the resource the message acted on.
    /// </summary>
    /// <param name="targetId">The resource identifier.</param>
    /// <returns>The scope, for chaining.</returns>
    IAuditScope WithTarget(string targetId);

    /// <summary>
    ///     Records the reason the action was taken.
    /// </summary>
    /// <param name="reason">The reason, as supplied by the caller or composed by the handler.</param>
    /// <returns>The scope, for chaining.</returns>
    IAuditScope WithReason(string reason);

    /// <summary>
    ///     Attaches an additional property to the audit record.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value.</param>
    /// <returns>The scope, for chaining.</returns>
    /// <remarks>
    ///     Properties carry non-identifying context such as a device identifier. Do not put personal data or payload
    ///     snapshots here: an audit trail that holds them becomes an erasure liability, and what changed is already
    ///     recorded by the domain event stream under its own retention rule.
    /// </remarks>
    IAuditScope WithProperty(string name, string value);
}
