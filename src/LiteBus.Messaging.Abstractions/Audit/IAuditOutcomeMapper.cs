namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Classifies how a mediation ended in the vocabulary of an audit trail.
/// </summary>
/// <remarks>
///     <para>
///         Most of the mapping needs no application knowledge, because the pipeline already distinguishes a refusal from
///         an early answer and from a fault. A guard denial is a denial, an early answer is a success, a fault is a
///         failure, and a cancellation is a cancellation.
///     </para>
///     <para>
///         What LiteBus cannot know is whether an exception was a refusal in disguise. An application that authorizes by
///         throwing owns that exception type, so it registers a mapper to have its refusal recorded as
///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />. Applications that refuse
///         through a guard or a validator need no mapper at all.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class UseCaseAuditOutcomeMapper : IAuditOutcomeMapper
/// {
///     public AuditOutcome Map(MessageCompletionContext context) => context.Outcome switch
///     {
///         MediationOutcome.Failed when context.Exception is ForbiddenException => AuditOutcome.Denied,
///         _ => DefaultAuditOutcomeMapper.MapByOutcome(context)
///     };
/// }
/// ]]></code>
/// </example>
public interface IAuditOutcomeMapper
{
    /// <summary>
    ///     Maps a completion context to the audit outcome recorded for it.
    /// </summary>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <returns>The audit outcome written to the record.</returns>
    AuditOutcome Map(MessageCompletionContext context);

    /// <summary>
    ///     Maps a completion context to the stable failure code recorded for a non-success outcome.
    /// </summary>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <returns>The failure code, or <see langword="null" /> when there is nothing to name.</returns>
    /// <remarks>
    ///     Defaults to the exception type name, which is useful before an application defines its own failure taxonomy.
    ///     A guard denial is deliberately left uncoded: <see cref="LiteBusMessageDeniedException" /> would only restate
    ///     the outcome, and the reason on the record already says why. That also keeps the two shapes of denial
    ///     consistent, since a refusal mapped to a value raises nothing at all.
    /// </remarks>
    string? MapFailureCode(MessageCompletionContext context)
    {
        return context.Exception is null or LiteBusMessageDeniedException
            ? null
            : context.Exception.GetType().Name;
    }
}
