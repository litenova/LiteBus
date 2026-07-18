namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes how durable stores and transactional writers treat duplicate idempotency keys.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ReturnExisting" /> returns the stored row and lets callers observe duplicate acceptance on
///         inbox receipts.
///     </para>
///     <para>
///         <see cref="Strict" /> fails the accept or enqueue operation when the idempotency key or supplied message
///         identifier already exists.
///     </para>
/// </remarks>
public enum IdempotencyConflictMode
{
    /// <summary>
    ///     Returns the existing stored row when the idempotency key or message identifier already exists.
    /// </summary>
    ReturnExisting = 0,

    /// <summary>
    ///     Fails the operation when the idempotency key or message identifier already exists.
    /// </summary>
    Strict = 1
}
