namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes idempotency metadata used to detect duplicate durable message submissions.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="Idempotency.None" /> when duplicate detection is not required for a message.
///         Use <see cref="Idempotency.Keyed" /> when callers can retry the same logical operation and the store should
///         return the existing row for the same key.
///     </para>
/// </remarks>
public abstract record Idempotency
{
    /// <summary>
    ///     Indicates that no idempotency key is supplied for the message.
    /// </summary>
    public sealed record None : Idempotency
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Idempotency.None" /> class.
        /// </summary>
        private None()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when duplicate detection is not requested.
        /// </summary>
        public static None Instance { get; } = new();
    }

    /// <summary>
    ///     Carries an application-defined idempotency key stored with the envelope.
    /// </summary>
    /// <param name="Key">The idempotency key used for insert-time deduplication.</param>
    /// <param name="ConflictMode">
    ///     How duplicate keys are handled. Defaults to <see cref="IdempotencyConflictMode.ReturnExisting" />.
    /// </param>
    public sealed record Keyed(string Key, IdempotencyConflictMode ConflictMode = IdempotencyConflictMode.ReturnExisting)
        : Idempotency;
}
