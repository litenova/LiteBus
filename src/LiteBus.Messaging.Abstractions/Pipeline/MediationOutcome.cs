namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes how a mediation operation ended.
/// </summary>
/// <remarks>
///     Every mediation reports exactly one outcome to registered completion handlers, including the paths that never
///     reach post-handlers or error handlers. The outcomes distinguish a refusal from an early answer and from a fault,
///     because a review of a trail asks a different question of each.
/// </remarks>
public enum MediationOutcome
{
    /// <summary>
    ///     The main handler and all post-handlers ran without raising an exception.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    ///     A shortcut answered the message because the result was already known, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     A cache hit and an idempotent command that detects it already ran are the usual cases. Nothing was refused,
    ///     so an audit trail records this as a success.
    /// </remarks>
    Answered = 1,

    /// <summary>
    ///     A guard refused the message, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     This is the outcome an audit trail records as a denial, and the one a security review asks about. It is
    ///     reachable only from the guard stage: suppressing post-handlers after the work has happened still reports
    ///     <see cref="Succeeded" />.
    /// </remarks>
    Denied = 2,

    /// <summary>
    ///     The pipeline raised an exception other than cancellation or denial.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     The pipeline was cancelled through the mediation cancellation token.
    /// </summary>
    Canceled = 4,

    /// <summary>
    ///     A validator reported the message malformed, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     An invalid message is kept apart from <see cref="Denied" /> for the same reason a denial is kept apart from a
    ///     failure: a security review reads denials, and malformed input is noise in that list. It is kept apart from
    ///     <see cref="Failed" /> because a validator reports a failure as a value rather than by throwing, so it is a
    ///     decision rather than a fault.
    /// </remarks>
    Invalid = 5
}
