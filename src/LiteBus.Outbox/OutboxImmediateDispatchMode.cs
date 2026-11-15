namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Chooses whether the caller of a command waits for immediate dispatch after the commit.
/// </summary>
public enum OutboxImmediateDispatchMode
{
    /// <summary>
    ///     The caller waits until the staged envelopes are dispatched or the timeout expires, so the next request sees
    ///     what the immediate handlers wrote.
    /// </summary>
    Await = 0,

    /// <summary>
    ///     Dispatch runs on a background task with its own scope after the commit, and the caller does not wait. The
    ///     outbox still delivers anything the background dispatch does not.
    /// </summary>
    Background = 1
}
