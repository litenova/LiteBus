namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     States the audit position of a message: either it produces an audit record, or it is deliberately exempt.
/// </summary>
/// <remarks>
///     <para>
///         This is the constant half of an audit record: the parts that are known without running anything. The variable
///         half, such as the identifier of a resource the handler created, is supplied at runtime through
///         <see cref="IAuditScope" />, or declared on <see cref="AuditedDeclaration" /> as a selector when it can be
///         read off the message itself.
///     </para>
///     <para>
///         The two positions are separate types rather than one type with a flag, so a declaration cannot hold a
///         combination that means nothing. An exemption has no category and no action; an audited message has no
///         exemption rationale. Match on <see cref="AuditedDeclaration" /> and <see cref="AuditExemptDeclaration" /> to
///         tell them apart.
///     </para>
///     <para>
///         Declare it either with <see cref="AuditedAttribute" /> and <see cref="AuditExemptAttribute" /> on the message,
///         or with an <c>IAuditDefinition&lt;TMessage&gt;</c> in a definition class beside the message. A definition wins
///         when both are present.
///     </para>
/// </remarks>
public abstract record AuditDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditDeclaration" /> class.
    /// </summary>
    /// <remarks>
    ///     The hierarchy is closed to the two positions LiteBus defines, so the constructor is not accessible outside
    ///     this assembly.
    /// </remarks>
    internal AuditDeclaration()
    {
    }

    /// <summary>
    ///     Gets a value indicating whether mediating the message produces an audit record.
    /// </summary>
    public abstract bool IsAudited { get; }

    /// <summary>
    ///     Creates a declaration for a message that produces an audit record.
    /// </summary>
    /// <param name="action">The use-case identity written to the record, such as <c>orders.place-order</c>.</param>
    /// <param name="category">The optional category written to the record, such as <c>money</c>.</param>
    /// <param name="targetKind">The optional kind of entity the message acts on, such as <c>order</c>.</param>
    /// <param name="reasonRequired">
    ///     <see langword="true" /> when mediation must carry a reason, which the audit step enforces.
    /// </param>
    /// <returns>
    ///     The audited declaration. Chain <c>WithTargetFrom&lt;TMessage&gt;</c> or <c>WithReasonFrom&lt;TMessage&gt;</c>
    ///     on it directly; no <c>with</c> expression is needed.
    /// </returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="action" /> is null or whitespace.</exception>
    /// <example>
    ///     <code>
    ///     public AuditDeclaration Audit =&gt;
    ///         AuditDeclaration.Audited("orders.cancel-order", category: "money", targetKind: "order")
    ///             .WithTargetFrom&lt;CancelOrderCommand&gt;(command =&gt; command.OrderId.ToString());
    ///     </code>
    /// </example>
    public static AuditedDeclaration Audited(
        string action,
        string? category = null,
        string? targetKind = null,
        bool reasonRequired = false)
    {
        return new AuditedDeclaration(action)
        {
            Category = category,
            TargetKind = targetKind,
            ReasonRequired = reasonRequired
        };
    }

    /// <summary>
    ///     Creates a declaration for a message that is deliberately not audited.
    /// </summary>
    /// <param name="rationale">The recorded reason the message is exempt.</param>
    /// <returns>The exempt declaration.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public static AuditExemptDeclaration Exempt(string rationale)
    {
        return new AuditExemptDeclaration(rationale);
    }
}
