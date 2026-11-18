namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The refusal codes the authorization policies report when a policy evaluation denies a message.
/// </summary>
/// <remarks>
///     <para>
///         Carried on the denial refusal, so <c>TrySendAsync</c> returns them and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes them as its
///         <see cref="LiteBusMessageDeniedException.Code" />. An HTTP boundary switches on them: a caller that is not
///         signed in is asked to authenticate (401), while a signed-in caller that lacks the permission is refused
///         (403).
///     </para>
/// </remarks>
public static class AuthorizationPolicyRefusalCodes
{
    /// <summary>
    ///     The code reported when the policy requires a signed-in caller and the mediation has none.
    /// </summary>
    public const string NotSignedIn = "authorization.not-signed-in";

    /// <summary>
    ///     The code reported when the caller does not hold the permission the policy requires.
    /// </summary>
    public const string MissingPermission = "authorization.missing-permission";

    /// <summary>
    ///     The code reported when a signed-in caller fails a policy check, such as a claim, ownership, or sign-in age
    ///     requirement.
    /// </summary>
    public const string Forbidden = "authorization.forbidden";

    /// <summary>
    ///     The code reported when the message declares a policy name no composition registered.
    /// </summary>
    public const string UnknownPolicy = "authorization.unknown-policy";
}
