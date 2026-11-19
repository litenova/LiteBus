using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     The input an <see cref="IAuditRecordMapper{TDocument}" /> reads besides the record: the evaluation the mapping
///     runs in.
/// </summary>
/// <param name="Evaluation">The evaluation the mapping runs in.</param>
/// <remarks>
///     <para>
///         Mapping runs in the mediation's scope before commit, so the mapper may resolve scoped services such as a
///         pseudonym store. <see cref="Evaluation" /> carries the session on its <c>Services</c> and the identity on
///         its <c>Identity</c>; the <see cref="Services" /> and <see cref="Identity" /> properties repeat them for
///         direct reads.
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
}
