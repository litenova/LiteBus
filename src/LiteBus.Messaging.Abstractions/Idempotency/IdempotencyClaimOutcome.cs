namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How a store answered an attempt to claim an idempotency key.
/// </summary>
/// <remarks>
///     There are two answers, not three. A delivery that is still being applied is not a third state the pipeline can
///     do anything sensible with: answering the caller would report work done that might still fail, and proceeding
///     would apply it twice. A store facing a concurrent claim either waits for the other transaction to settle, which
///     a primary-key insert does for free, or throws its own conflict exception for the caller to retry.
/// </remarks>
public enum IdempotencyClaimOutcome
{
    /// <summary>
    ///     The key was not held, and the caller now holds it. The message runs.
    /// </summary>
    Granted = 0,

    /// <summary>
    ///     The key was applied by an earlier delivery. The message is answered without running.
    /// </summary>
    AlreadyCompleted = 1
}
