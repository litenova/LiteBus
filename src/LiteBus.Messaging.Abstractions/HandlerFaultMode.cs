namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Controls how a failing handler affects the sibling handlers broadcast alongside it.
/// </summary>
/// <remarks>
///     <para>
///         This is a policy about fault isolation, not about concurrency, so it applies to sequential and parallel
///         execution alike. A broadcast that stops at the first failure loses the handlers behind it, which for an
///         event with independent subscribers is usually the wrong answer: the subscriber that failed is rarely the
///         one the others depend on.
///     </para>
///     <para>
///         The mode governs handlers within one priority group. It never carries across priority groups, because a
///         priority difference is the explicit statement that the later group runs after the earlier one, and
///         continuing past a failed group would run handlers whose precondition did not hold.
///     </para>
/// </remarks>
public enum HandlerFaultMode
{
    /// <summary>
    ///     One handler failure stops the group. Handlers already started settle before the failure is propagated;
    ///     handlers not yet started do not run.
    /// </summary>
    PropagateFirst = 0,

    /// <summary>
    ///     Every handler in the group runs regardless of its siblings, and the failures are aggregated afterwards.
    /// </summary>
    /// <remarks>
    ///     A single failure is rethrown as itself; two or more are wrapped in an <see cref="System.AggregateException" />.
    /// </remarks>
    ContinueAndAggregate = 1
}
