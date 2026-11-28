namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     What the staging handler does with a domain event type that has no registered decision.
/// </summary>
public enum UndecidedDomainEventPolicy
{
    /// <summary>
    ///     Stage the event as-is. This is the current behavior and the default.
    /// </summary>
    StageAsIs = 0,

    /// <summary>
    ///     Drop the event without staging it.
    /// </summary>
    Ignore = 1,

    /// <summary>
    ///     Throw before the commit, naming the event type and the missing decision.
    /// </summary>
    Throw = 2,
}
