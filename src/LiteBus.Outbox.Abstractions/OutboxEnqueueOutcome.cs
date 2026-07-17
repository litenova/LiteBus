namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes whether an outbox enqueue operation created a new row or returned an existing one.
/// </summary>
public enum OutboxEnqueueOutcome
{
    /// <summary>
    ///     The store accepted a new outbox envelope for the supplied enqueue item.
    /// </summary>
    Enqueued = 0,

    /// <summary>
    ///     The store returned an existing outbox envelope for the supplied idempotency key or message identifier.
    /// </summary>
    AlreadyEnqueued = 1
}
