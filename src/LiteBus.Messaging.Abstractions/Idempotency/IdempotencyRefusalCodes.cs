namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The refusal codes the command pipeline reports when an idempotency claim is denied.
/// </summary>
/// <remarks>
///     <para>
///         Carried on the denial refusal, so <c>TrySendAsync</c> returns them and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes them as its
///         <see cref="LiteBusMessageDeniedException.Code" />. An HTTP boundary switches on them: an in-flight claim is a
///         conflict the client retries (409), while a fingerprint mismatch is a request that can never succeed (422).
///     </para>
/// </remarks>
public static class IdempotencyRefusalCodes
{
    /// <summary>
    ///     The code reported when another delivery holds the key and has not settled yet.
    /// </summary>
    public const string InFlight = "idempotency.in-flight";

    /// <summary>
    ///     The code reported when the key was already applied with different content.
    /// </summary>
    public const string FingerprintMismatch = "idempotency.fingerprint-mismatch";
}
