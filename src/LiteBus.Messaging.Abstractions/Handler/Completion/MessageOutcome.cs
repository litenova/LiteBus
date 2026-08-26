namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes how a mediation operation ended.
/// </summary>
/// <remarks>
///     Every mediation reports exactly one outcome to registered completion handlers, including the paths that never
///     reach post-handlers or error handlers. The outcomes distinguish a refusal from an early answer and from a fault,
///     because a review of a trail asks a different question of each.
/// </remarks>
public enum MessageOutcome
{
    /// <summary>
    ///     The main handler and all post-handlers ran without raising an exception.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    ///     A gate stopped the pipeline because the result was already known, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     A cache hit and an idempotent command that detects it already ran are the usual cases. Nothing was refused,
    ///     so an audit trail records this as a success.
    /// </remarks>
    ShortCircuited = 1,

    /// <summary>
    ///     A gate refused the message, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     This is the outcome an audit trail records as a denial, and the one a security review asks about. It is
    ///     reachable only from the pre-handler stage: suppressing post-handlers after the work has happened still
    ///     reports <see cref="Succeeded" />.
    /// </remarks>
    Denied = 2,

    /// <summary>
    ///     The pipeline raised an exception other than cancellation or denial.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     The pipeline was cancelled through the mediation cancellation token.
    /// </summary>
    Canceled = 4
}
