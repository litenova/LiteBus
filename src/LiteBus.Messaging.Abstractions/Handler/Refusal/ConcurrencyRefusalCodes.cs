namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The refusal code fault classifiers report when a commit loses an optimistic concurrency race.
/// </summary>
/// <remarks>
///     <para>
///         Carried on the denial refusal, so <c>TrySendAsync</c> returns it and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes it as its
///         <see cref="LiteBusMessageDeniedException.Code" />. The message was well-formed, but the state it was
///         computed from is no longer current, so the caller reloads and retries.
///     </para>
/// </remarks>
public static class ConcurrencyRefusalCodes
{
    /// <summary>
    ///     The code reported when another writer changed the state the command was computed from.
    /// </summary>
    public const string Conflict = "concurrency.conflict";
}
