using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One entry in an audit trail: an actor attempted an action against a resource, and this is how it ended.
/// </summary>
/// <remarks>
///     <para>
///         The shape follows the model that NIST SP 800-53 AU-3, PCI DSS Requirement 10 and the DMTF CADF event model
///         all describe: an initiator performs an action on a target, producing an outcome, observed at a time and from
///         a place. Building to that shape costs nothing and lets the trail map onto a SIEM schema later without being
///         remodelled.
///     </para>
///     <para>
///         This record is a handoff to <see cref="IAuditTrail" />, not a persistence schema. What a store writes to disk,
///         including any integrity chaining and column layout, is the store's own concern.
///     </para>
///     <para>
///         Note what is deliberately absent: any before-and-after snapshot of the changed state. That is the field which
///         turns an audit table into an erasure liability under data-protection law, and it is redundant, because the
///         domain event stream already records what changed. The trail records who is answerable and why.
///     </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>
    ///     Gets the use-case identity of the action, such as <c>orders.place-order</c>.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    ///     Gets how the action ended.
    /// </summary>
    /// <remarks>
    ///     Recording refusals and failures is the point. A trail of successes is a changelog, not an audit, and the
    ///     question a review opens with is who attempted something and was stopped.
    /// </remarks>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets the time the action occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    ///     Gets how long the mediation took.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    ///     Gets the category used to group the record for review and retention.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    ///     Gets the kind of resource acted on, such as <c>order</c>.
    /// </summary>
    public string? TargetKind { get; init; }

    /// <summary>
    ///     Gets the identifier of the resource acted on.
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    ///     Gets the reason the action was taken, where the action requires one.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the stable failure code for a non-success outcome.
    /// </summary>
    /// <remarks>
    ///     Defaults to the exception type name when the mediation failed, so a trail is useful before an application
    ///     defines its own failure taxonomy. A guard denial carries no code, because <see cref="Outcome" /> and
    ///     <see cref="Reason" /> already say what happened and why.
    /// </remarks>
    public string? FailureCode { get; init; }

    /// <summary>
    ///     Gets the CLR type name of the message that produced this record, for correlating with application logs.
    /// </summary>
    public string? MessageType { get; init; }

    /// <summary>
    ///     Gets the correlation identifier tying this record to the diagnostic log lines for the same request.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    ///     Gets the tenant the action happened in.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    ///     Gets the additional non-identifying properties attached by the handler.
    /// </summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
