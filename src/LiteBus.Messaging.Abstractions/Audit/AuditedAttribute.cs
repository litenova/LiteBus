using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that mediating the annotated message produces an audit record.
/// </summary>
/// <remarks>
///     <para>
///         The attribute is the lightweight form of an audit declaration, for messages whose configuration is a single
///         fact. When a message needs richer or compile-checked configuration, declare an
///         <c>IAuditDefinition&lt;TMessage&gt;</c> in a definition class instead. A definition wins when both are
///         present, because both contribute the same <see cref="AuditDeclaration" /> and the definition is applied last.
///     </para>
///     <para>
///         Pair with <see cref="AuditExemptAttribute" /> so that every message states its position explicitly, rather
///         than an unaudited message being indistinguishable from one nobody considered.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [Audited("orders.place-order", Category = "money", TargetKind = "order")]
/// public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(AuditDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AuditedAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedAttribute" /> class.
    /// </summary>
    /// <param name="action">The use-case identity written to the audit record, such as <c>orders.place-order</c>.</param>
    public AuditedAttribute(string action)
    {
        Action = action;
    }

    /// <summary>
    ///     Gets the use-case identity written to the audit record.
    /// </summary>
    public string Action { get; }

    /// <summary>
    ///     Gets or sets the category used to group the record for review and retention.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    ///     Gets or sets the kind of resource the message acts on.
    /// </summary>
    public string? TargetKind { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the handler must supply a reason through
    ///     <see cref="IAuditScope.WithReason" />.
    /// </summary>
    public bool ReasonRequired { get; set; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(AuditDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return AuditDeclaration.Audited(Action) with
        {
            Category = Category,
            TargetKind = TargetKind,
            ReasonRequired = ReasonRequired
        };
    }
}
