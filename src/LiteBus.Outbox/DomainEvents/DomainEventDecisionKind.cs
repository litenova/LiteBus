namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     What the staging handler does with one decided domain event type.
/// </summary>
public enum DomainEventDecisionKind
{
    /// <summary>
    ///     Translate the event into the messages to stage, through a function or a translator.
    /// </summary>
    Translate = 0,

    /// <summary>
    ///     Drop the event without staging it.
    /// </summary>
    Ignore = 1,

    /// <summary>
    ///     Publish the event in process after the commit.
    /// </summary>
    PublishInProcess = 2,

    /// <summary>
    ///     Stage the event unchanged, in its collected shape.
    /// </summary>
    Stage = 3,
}
