using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that mediating a message produces an audit record, and the fixed facts about that record.
/// </summary>
/// <remarks>
///     <para>
///         Create it through <see cref="AuditDeclaration.Audited" /> and refine it with <c>with</c>, so the required
///         action is always supplied and the optional facts read as what they are.
///     </para>
///     <para>
///         When the target or the reason can be read off the message itself, declare that with
///         <c>WithTargetFrom&lt;TMessage&gt;</c> and <c>WithReasonFrom&lt;TMessage&gt;</c> rather than pushing it through
///         <see cref="IAuditScope" /> in the handler. What the scope supplies still wins when both are present, and a
///         selector that throws fails the record write rather than producing a record without the value.
///     </para>
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
    ///     Gets a value indicating whether the record must carry a reason, supplied by the handler, the pipeline
    ///     decision, or a selector declared on the message.
    /// </summary>
    /// <remarks>
    ///     Some actions are only accountable with a justification, such as an operator overriding a price. When this is
    ///     set and no reason reaches the record on a success, the writer raises
    ///     <see cref="AuditReasonMissingException" /> rather than writing an incomplete record, because a required
    ///     justification that silently goes missing defeats the requirement.
    /// </remarks>
    public bool ReasonRequired { get; init; }

    /// <summary>
    ///     Gets the projection from the message to the identifier of the resource the message acted on.
    /// </summary>
    /// <value>
    ///     The selector, or <see langword="null" /> when the target comes only from
    ///     <see cref="IAuditScope.WithTarget" />. A selector result that is null or blank means the message carries no
    ///     target, and the record keeps no target.
    /// </value>
    /// <remarks>
    ///     Set it through <c>WithTargetFrom&lt;TMessage&gt;</c> so the projection is typed to the message. The scope
    ///     wins when both supply a value, because the handler knows what it did and the message only knows what it
    ///     asked for. Attributes cannot carry delegates, so this is only available on definitions.
    /// </remarks>
    public Func<object, string?>? TargetSelector { get; init; }

    /// <summary>
    ///     Gets the projection from the message to the reason the action was taken.
    /// </summary>
    /// <value>
    ///     The selector, or <see langword="null" /> when the reason comes only from
    ///     <see cref="IAuditScope.WithReason" /> or the pipeline decision. A selector result that is null or blank
    ///     means the message carries no reason, and <see cref="ReasonRequired" /> still applies to the final value.
    /// </value>
    /// <remarks>
    ///     Set it through <c>WithReasonFrom&lt;TMessage&gt;</c> so the projection is typed to the message. The scope
    ///     wins when both supply a value, and the pipeline decision reason wins over the selector, because a denial or
    ///     an answer explanation describes the ending while the selector describes the intent. Attributes cannot carry
    ///     delegates, so this is only available on definitions.
    /// </remarks>
    public Func<object, string?>? ReasonSelector { get; init; }

    /// <summary>
    ///     Declares that the target identifier is read off the message.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="targetSelector">Projects the target identifier from the message.</param>
    /// <returns>A copy of this declaration that reads the target from the message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="targetSelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for. Keep the selector a pure projection: it runs at the completion stage, when the mediation has
    ///     already ended, and a selector that throws fails the record write.
    /// </remarks>
    public AuditedDeclaration WithTargetFrom<TMessage>(Func<TMessage, string?> targetSelector)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        return this with { TargetSelector = message => targetSelector((TMessage) message) };
    }

    /// <summary>
    ///     Declares that the reason is read off the message.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="reasonSelector">Projects the reason from the message.</param>
    /// <returns>A copy of this declaration that reads the reason from the message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reasonSelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for. Keep the selector a pure projection: it runs at the completion stage, when the mediation has
    ///     already ended, and a selector that throws fails the record write.
    /// </remarks>
    public AuditedDeclaration WithReasonFrom<TMessage>(Func<TMessage, string?> reasonSelector)
    {
        ArgumentNullException.ThrowIfNull(reasonSelector);
        return this with { ReasonSelector = message => reasonSelector((TMessage) message) };
    }
}
