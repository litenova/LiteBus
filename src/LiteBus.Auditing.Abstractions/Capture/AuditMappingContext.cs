using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The input an <see cref="IAuditRecordMapper{TDocument}" /> reads besides the record: the evaluation the mapping
///     runs in and the store's write session.
/// </summary>
/// <param name="Evaluation">The evaluation the mapping runs in.</param>
/// <remarks>
///     <para>
///         Every audit store awaits <see cref="IAuditRecordMapper{TDocument}.MapAsync" /> with this context, on the
///         success path and on the out-of-band path that records denials, failures and cancellations. The mapper is
///         resolved from the mediation's scope, so its constructor may take scoped services, and
///         <see cref="Services" /> is that same scope.
///     </para>
///     <para>
///         <see cref="Evaluation" /> describes the mediation that produced the record: its message, identity, clock
///         and execution context. A record written outside a mediation, such as one delivered through the outbox
///         capture mode or written to <see cref="IAuditTrail" /> directly by application code, is mapped in the scope
///         that writes it, with the record itself as the message.
///     </para>
///     <para>
///         <see cref="Features" /> holds the store's write session, so a mapper that creates a row, such as a
///         pseudonym for the actor, writes it in the same transaction as the record.
///     </para>
/// </remarks>
public sealed record AuditMappingContext(MessageEvaluationContext Evaluation)
{
    /// <summary>
    ///     Gets the mediation's scope the mapping resolves services from.
    /// </summary>
    public IServiceProvider Services => Evaluation.Services;

    /// <summary>
    ///     Gets who the mediation runs for.
    /// </summary>
    public ExecutionIdentity Identity => Evaluation.Identity;

    /// <summary>
    ///     Gets the store-specific values the store offers for this mapping, such as its write session.
    /// </summary>
    public AuditMappingFeatures Features { get; init; } = new();
}
