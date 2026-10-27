namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The answer an <see cref="IIdempotencyStore" /> gives when a message tries to claim its key.
/// </summary>
/// <param name="Outcome">How the store answered the claim.</param>
/// <param name="Payload">
///     The serialized result the first attempt recorded, when the outcome is
///     <see cref="IdempotencyClaimOutcome.AlreadyCompleted" /> and the declaration asked for the result to be replayed.
/// </param>
public readonly record struct IdempotencyClaim(IdempotencyClaimOutcome Outcome, string? Payload = null)
{
    /// <summary>
    ///     Gets a claim the caller now owns, meaning the message has not run before.
    /// </summary>
    public static IdempotencyClaim Granted => new(IdempotencyClaimOutcome.Granted);

    /// <summary>
    ///     Gets a claim refused because another delivery holds the key and has not settled yet.
    /// </summary>
    public static IdempotencyClaim InFlight => new(IdempotencyClaimOutcome.InFlight);

    /// <summary>
    ///     Gets a claim refused because the key was already applied with different content.
    /// </summary>
    public static IdempotencyClaim FingerprintMismatch => new(IdempotencyClaimOutcome.FingerprintMismatch);

    /// <summary>
    ///     Creates a claim refused because the key has already been applied.
    /// </summary>
    /// <param name="payload">The serialized result recorded by the first attempt, when one was recorded.</param>
    /// <returns>The claim.</returns>
    public static IdempotencyClaim AlreadyCompleted(string? payload = null)
    {
        return new IdempotencyClaim(IdempotencyClaimOutcome.AlreadyCompleted, payload);
    }
}
