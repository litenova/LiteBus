namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Classifies how a mediation ended in the vocabulary of an audit trail.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus knows that a mediation failed; it cannot know whether it failed because the actor was not permitted.
///         That distinction is the one a security review cares about most, and the exception that carries it belongs to
///         the application. Register a mapper to make it.
///     </para>
///     <para>
///         The default mapping treats an aborted mediation as a denial, because aborting is how a pre-handler refuses to
///         let a message proceed, and every other failure as a failure.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class UseCaseAuditOutcomeMapper : IAuditOutcomeMapper
/// {
///     public AuditOutcome Map(MessageCompletionContext context) => context.Outcome switch
///     {
///         MessageOutcome.Failed when context.Exception is ForbiddenException => AuditOutcome.Denied,
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
    /// <returns>The failure code, or <see langword="null" /> when the action succeeded.</returns>
    /// <remarks>
    ///     Defaults to the exception type name, which is useful before an application defines its own failure taxonomy.
    /// </remarks>
    string? MapFailureCode(MessageCompletionContext context)
    {
        return context?.Exception?.GetType().Name;
    }
}
