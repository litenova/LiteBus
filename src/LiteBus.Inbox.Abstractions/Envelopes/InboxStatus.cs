namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents the processing status of a persisted inbox message.
/// </summary>
public enum InboxStatus
{
    /// <summary>
    ///     The message is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     The message has been leased by a processor.
    /// </summary>
    Processing = 1,

    /// <summary>
    ///     The message completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    ///     The message failed and may be retried.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     The message exceeded retry policy or was manually moved aside.
    /// </summary>
    DeadLettered = 4
}