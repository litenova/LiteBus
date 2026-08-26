namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes how a mediation operation ended.
/// </summary>
/// <remarks>
///     Every mediation reports exactly one outcome to registered completion handlers, including the paths that never
///     reach post-handlers or error handlers.
/// </remarks>
public enum MessageOutcome
{
    /// <summary>
    ///     The main handler and all post-handlers ran without raising an exception.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    ///     A short-circuiting pre-handler stopped the pipeline, so the main handler never ran.
    /// </summary>
    /// <remarks>
    ///     This is the outcome an audit trail records as a denial. It is reachable only from the pre-handler stage:
    ///     suppressing post-handlers after the work has happened still reports <see cref="Succeeded" />.
    /// </remarks>
    Aborted = 1,

    /// <summary>
    ///     The pipeline raised an exception other than cancellation.
    /// </summary>
    Failed = 2,

    /// <summary>
    ///     The pipeline was cancelled through the mediation cancellation token.
    /// </summary>
    Canceled = 3
}
