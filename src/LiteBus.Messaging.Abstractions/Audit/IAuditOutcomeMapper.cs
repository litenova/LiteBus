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
    ///     <para>
    ///         A refusal's own code wins, because a guard or a validator that passed one chose it deliberately and it
    ///         is stable across wordings. It also survives either shape of refusal: a refusal mapped to a value raises
    ///         nothing at all, so an exception-derived code would be present for one shape and absent for the other.
    ///     </para>
    ///     <para>
    ///         Only a refusal contributes its code. A shortcut answers with a code too, and that mediation reports
    ///         <see cref="AuditOutcome.Succeeded" />, so carrying it here would put a cache-hit code in the field a
    ///         review reads as the reason something did not work.
    ///     </para>
    ///     <para>
    ///         Otherwise it defaults to the exception type name, which is useful before an application defines its own
    ///         failure taxonomy. A refusal that supplied no code stays uncoded rather than reporting
    ///         <see cref="LiteBusMessageDeniedException" /> or <see cref="LiteBusMessageInvalidException" />: naming
    ///         those would only restate <see cref="MessageCompletionContext.Outcome" />, and the reason on the record
    ///         already says why.
    ///     </para>
    /// </remarks>
    string? MapFailureCode(MessageCompletionContext context)
    {
        if (context.Outcome is MediationOutcome.Denied or MediationOutcome.Invalid && context.Code is not null)
        {
            return context.Code;
        }

        return context.Exception is null or LiteBusMessageDeniedException or LiteBusMessageInvalidException
            ? null
            : context.Exception.GetType().Name;
    }
}
