namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes what a <see cref="PipelineDirective" /> tells the pipeline to do.
/// </summary>
/// <remarks>
///     Stopping the pipeline covers two different events, and an audit trail must not confuse them. A refusal says the
///     actor was not permitted to do this. An early answer says the result was already known, so running the handler
///     would have been redundant. The kind is what keeps them apart from the gate all the way to the recorded outcome.
/// </remarks>
public enum PipelineDirectiveKind
{
    /// <summary>
    ///     The pipeline proceeds to the next stage.
    /// </summary>
    Continue = 0,

    /// <summary>
    ///     The pipeline stops because the result is already known, and the main handler would add nothing.
    /// </summary>
    /// <remarks>
    ///     A cache hit and an idempotent command that detects it already ran are the usual cases. The mediation reports
    ///     <see cref="MessageOutcome.ShortCircuited" />, which an audit trail records as a success, because nothing was
    ///     refused.
    /// </remarks>
    ShortCircuit = 1,

    /// <summary>
    ///     The pipeline stops because the message is refused.
    /// </summary>
    /// <remarks>
    ///     The mediation reports <see cref="MessageOutcome.Denied" />, which an audit trail records as a denial. A
    ///     denial always carries a reason.
    /// </remarks>
    Deny = 2
}
