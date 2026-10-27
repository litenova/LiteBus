using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Remembers which idempotency keys have been applied.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus decides when a key is claimed and what happens to a repeat. Where the key is remembered, and with
///         what atomicity, is the application's decision, so this contract is deliberately small.
///     </para>
///     <para>
///         <see cref="TryClaimAsync" /> has to be atomic, and the way to get that is to let the storage engine refuse
///         the duplicate: insert the key and treat a primary-key violation as the refusal. Reading first and then
///         writing loses the race idempotency exists to win, because two concurrent deliveries both read nothing.
///     </para>
///     <para>
///         The claim should also share the transaction that applies the change, or a crash between the two leaves a key
///         claimed for work that never happened. Claim through the same unit of work the handler writes through and let
///         it commit both. See <see cref="HandlerPriorities.UnitOfWork" /> for where that commit goes.
///     </para>
///     <para>
///         Nothing here expires an applied key. A store that grows forever is a storage problem with a storage answer, a
///         retention job, and only the application knows how long a repeat is still plausible. An unsettled claim is
///         different: a claimant that crashes without settling would block its key forever, so a store takes the claim
///         over for a new caller once it is older than the store's claim timeout.
///     </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    ///     Attempts to claim a key for a message about to run.
    /// </summary>
    /// <param name="key">The scoped idempotency key.</param>
    /// <param name="fingerprint">
    ///     The fingerprint of the message content, or <see langword="null" /> when the declaration selects none. A new
    ///     key is claimed with its fingerprint. An applied key answers
    ///     <see cref="IdempotencyClaimOutcome.AlreadyCompleted" /> when the fingerprint matches or either fingerprint is
    ///     <see langword="null" />, and <see cref="IdempotencyClaimOutcome.FingerprintMismatch" /> when both are
    ///     non-null and differ.
    /// </param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     <see cref="IdempotencyClaimOutcome.Granted" /> when the caller now holds the key, either because the key was
    ///     new or because an unsettled claim older than the store's claim timeout was taken over;
    ///     <see cref="IdempotencyClaimOutcome.AlreadyCompleted" /> with the recorded result when the key was already
    ///     applied; <see cref="IdempotencyClaimOutcome.InFlight" /> when another delivery holds the key and has not
    ///     settled yet; <see cref="IdempotencyClaimOutcome.FingerprintMismatch" /> when the key was applied with
    ///     different content.
    /// </returns>
    Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Marks a claimed key as applied, recording the result when the declaration asked for it to be replayed.
    /// </summary>
    /// <param name="key">The scoped idempotency key.</param>
    /// <param name="payload">The serialized result to replay to a repeat, or <see langword="null" /> to record none.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Releases a claimed key whose message did not succeed, so the work can be attempted again.
    /// </summary>
    /// <param name="key">The scoped idempotency key.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    /// <remarks>
    ///     Without this a transient failure would burn the key, and the retry would be answered as already applied,
    ///     which is the opposite of what idempotency is for.
    /// </remarks>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}
