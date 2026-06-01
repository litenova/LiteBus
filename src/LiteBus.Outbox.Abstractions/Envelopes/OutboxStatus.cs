namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents the publication status of an outbox message.
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    ///     The message is waiting to be published.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     The message has been leased by a publisher.
    /// </summary>
    Publishing = 1,

    /// <summary>
    ///     The message was published successfully.
    /// </summary>
    Published = 2,

    /// <summary>
    ///     The message failed and may be retried.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     The message exceeded retry policy or was manually moved aside.
    /// </summary>
    DeadLettered = 4
}