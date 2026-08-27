namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How a pre stage treats the decisions its handlers return.
/// </summary>
/// <remarks>
///     The policy follows from who reads the answer. A caller who is not allowed to proceed gets one reason, and
///     enumerating what else is wrong would tell them more about the system than they should learn. A caller holding a
///     malformed message wants every problem at once so they can fix it in one pass.
/// </remarks>
internal enum StageAggregation
{
    /// <summary>
    ///     The stage ends as soon as one handler stops the pipeline, and the rest do not run.
    /// </summary>
    StopAtFirst = 0,

    /// <summary>
    ///     Every handler in the stage runs, and their reported failures are gathered into one decision.
    /// </summary>
    CollectFailures = 1
}
