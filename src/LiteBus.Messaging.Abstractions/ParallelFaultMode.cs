namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Controls how parallel handler execution surfaces failures to callers.
/// </summary>
public enum ParallelFaultMode
{
    /// <summary>
    ///     The first handler failure cancels sibling tasks and propagates immediately.
    /// </summary>
    PropagateFirst = 0,

    /// <summary>
    ///     All handlers in the group run to completion and failures are aggregated.
    /// </summary>
    AggregateAll = 1
}
