using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that mediating a message produces an audit record, and the fixed facts about that record.
/// </summary>
/// <remarks>
///     Create it through <see cref="AuditDeclaration.Audited" /> and refine it with <c>with</c>, so the required action
///     is always supplied and the optional facts read as what they are.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// AuditDeclaration.Audited("orders.place-order") with
/// {
///     Category = "money",
///     TargetKind = "order",
///     ReasonRequired = false
/// }
/// ]]></code>
/// </example>
public sealed record AuditedDeclaration : AuditDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedDeclaration" /> class.
    /// </summary>
    /// <param name="action">The use-case identity written to the audit record.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action" /> is null or whitespace.</exception>
    public AuditedDeclaration(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        Action = action;
    }

    /// <inheritdoc />
    public override bool IsAudited => true;

    /// <summary>
    ///     Gets the use-case identity written to the audit record, such as <c>orders.place-order</c>.
    /// </summary>
    /// <remarks>
    ///     This is the identity of the use case, not of the domain fact. A trail of use cases answers who was allowed to
    ///     do what; a trail of domain facts is the event stream, which already exists.
    /// </remarks>
    public string Action { get; init; }

    /// <summary>
    ///     Gets the category used to group the record for review and retention, such as <c>security</c> or <c>money</c>.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    ///     Gets the kind of resource the message acts on, such as <c>order</c>.
    /// </summary>
    public string? TargetKind { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the handler must supply a reason through <see cref="IAuditScope.WithReason" />.
    /// </summary>
    /// <remarks>
    ///     Some actions are only accountable with a justification, such as an operator overriding a price. When this is
    ///     set and no reason reaches the record, the writer raises
    ///     <see cref="LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException" /> rather than writing an
    ///     incomplete record, because a required justification that silently goes missing defeats the requirement.
    /// </remarks>
    public bool ReasonRequired { get; init; }
}
