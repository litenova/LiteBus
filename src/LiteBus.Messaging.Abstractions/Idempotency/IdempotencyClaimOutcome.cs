namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     How a store answered an attempt to claim an idempotency key.
/// </summary>
/// <remarks>
///     <para>
///         Four answers, because an HTTP caller needs three different refusals told apart. <see cref="Granted" /> runs
///         the message and <see cref="AlreadyCompleted" /> answers the repeat from the stored record. The other two end
///         the mediation as a denial: <see cref="InFlight" /> tells the caller the first delivery is still running, so
///         retrying later is the right move, and <see cref="FingerprintMismatch" /> tells the caller the key was reused
///         for different content, so retrying the same request can never succeed.
///     </para>
///     <para>
///         An unsettled claim is not held forever. A claimant that crashes without settling would otherwise block its key
///         until an operator intervenes, so a store lets a new caller take over a claim older than its claim timeout and
///         answers that caller <see cref="Granted" />. The timeout is long enough that a live claimant finishes first,
///         which is what keeps the takeover from granting a key twice.
///     </para>
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
    AlreadyCompleted = 1,

    /// <summary>
    ///     The key is held by a delivery that has not settled yet. The message is denied, and the caller retries after
    ///     the first delivery settles.
    /// </summary>
    InFlight = 2,

    /// <summary>
    ///     The key was applied by an earlier delivery carrying different content. The message is denied, and retrying
    ///     the same request is pointless: the key identifies other content now.
    /// </summary>
    FingerprintMismatch = 3
}
