using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares whether a message is audited, and the fixed facts about the audit record it produces.
/// </summary>
/// <remarks>
///     <para>
///         This is the constant half of an audit record: the parts that are known without running anything. The variable
///         half, such as the identifier of a resource the handler created, is supplied at runtime through
///         <see cref="IAuditScope" />.
///     </para>
///     <para>
///         Declare it either with <see cref="AuditedAttribute" /> and <see cref="AuditExemptAttribute" /> on the message,
///         or with an <c>IAuditDefinition&lt;TMessage&gt;</c> facet in a definition class beside the message. A
///         definition wins when both are present.
///     </para>
/// </remarks>
public sealed record AuditDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditDeclaration" /> class.
    /// </summary>
    /// <param name="isAudited">Whether the message produces an audit record.</param>
    /// <param name="action">The use-case identity written to the record, when audited.</param>
    /// <param name="rationale">The recorded reason for exempting the message, when not audited.</param>
    private AuditDeclaration(bool isAudited, string? action, string? rationale)
    {
        IsAudited = isAudited;
        Action = action;
        Rationale = rationale;
    }

    /// <summary>
    ///     Gets a value indicating whether mediating this message produces an audit record.
    /// </summary>
    public bool IsAudited { get; }

    /// <summary>
    ///     Gets the use-case identity written to the audit record, such as <c>orders.place-order</c>.
    /// </summary>
    /// <remarks>
    ///     This is the identity of the use case, not of the domain fact. It is <see langword="null" /> when
    ///     <see cref="IsAudited" /> is <see langword="false" />.
    /// </remarks>
    public string? Action { get; }

    /// <summary>
    ///     Gets the recorded reason the message is not audited.
    /// </summary>
    /// <remarks>
    ///     Auditing standards ask for the selection of audited events to be documented along with its rationale.
    ///     Recording the reason next to the code keeps that rationale from drifting away from what it describes.
    ///     It is <see langword="null" /> when <see cref="IsAudited" /> is <see langword="true" />.
    /// </remarks>
    public string? Rationale { get; }

    /// <summary>
    ///     Gets the category used to group the record for review and retention, such as <c>security</c> or <c>money</c>.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    ///     Gets the kind of resource the message acts on, such as <c>order</c>.
    /// </summary>
    public string? TargetKind { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the handler must supply a reason through <see cref="IAuditScope.Reason" />.
    /// </summary>
    public bool ReasonRequired { get; init; }

    /// <summary>
    ///     Creates a declaration for a message that produces an audit record.
    /// </summary>
    /// <param name="action">The use-case identity written to the record.</param>
    /// <returns>The audited declaration.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="action" /> is null or whitespace.</exception>
    public static AuditDeclaration Audited(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return new AuditDeclaration(isAudited: true, action, rationale: null);
    }

    /// <summary>
    ///     Creates a declaration for a message that is deliberately not audited.
    /// </summary>
    /// <param name="rationale">The recorded reason the message is exempt.</param>
    /// <returns>The exempt declaration.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public static AuditDeclaration Exempt(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        return new AuditDeclaration(isAudited: false, action: null, rationale);
    }
}
