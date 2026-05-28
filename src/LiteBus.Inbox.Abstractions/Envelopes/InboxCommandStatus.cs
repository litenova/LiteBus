namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Represents the processing status of a persisted inbox command.
/// </summary>
public enum InboxCommandStatus
{
    /// <summary>
    ///     The command is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     The command has been leased by a processor.
    /// </summary>
    Processing = 1,

    /// <summary>
    ///     The command completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    ///     The command failed and may be retried.
    /// </summary>
    Failed = 3,

    /// <summary>
    ///     The command exceeded retry policy or was manually moved aside.
    /// </summary>
    DeadLettered = 4
}