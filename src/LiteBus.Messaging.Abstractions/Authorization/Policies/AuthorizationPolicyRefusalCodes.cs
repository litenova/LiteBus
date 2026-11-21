namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The refusal and fault codes the authorization policies report when a policy evaluation ends without allowing
///     the message.
/// </summary>
/// <remarks>
///     <para>
///         Denial codes are carried on <see cref="PolicyDeny" />, so <c>TrySendAsync</c> returns them and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes them as its
///         <see cref="LiteBusMessageDeniedException.Code" />. An HTTP boundary switches on them: a caller that is not
///         signed in is asked to authenticate (401), while a signed-in caller that lacks the permission is refused
///         (403).
///     </para>
///     <para>
///         Fault codes are carried on <see cref="PolicyFault" />: a missing port or an unknown sign-in instant is a
///         configuration or dependency failure an operator must fix, not a refusal the caller can act on. The policy
///         guard raises <see cref="MessageFaultException" /> carrying the code, and transports answer it as a server
///         error. <c>WithRefusal</c> rewrites denials only and passes faults through unchanged, so an application
///         code never masks one.
///     </para>
/// </remarks>
public static class AuthorizationPolicyRefusalCodes
{
    /// <summary>
    ///     The denial code reported when the policy requires a signed-in caller and the mediation has none.
    /// </summary>
    public const string NotSignedIn = "authorization.not-signed-in";

    /// <summary>
    ///     The denial code reported when the caller does not hold the permission the policy requires.
    /// </summary>
    public const string MissingPermission = "authorization.missing-permission";

    /// <summary>
    ///     The denial code reported when a signed-in caller fails a policy check, such as a claim, ownership, or
    ///     sign-in age requirement.
    /// </summary>
    public const string Forbidden = "authorization.forbidden";

    /// <summary>
    ///     The fault code reported when the message declares a policy name no composition registered.
    /// </summary>
    /// <remarks>
    ///     Composition fails naming the name and the messages that declare it, so a mediation normally never reaches
    ///     this path. A guard that does raises <see cref="MessageFaultException" /> carrying this code.
    /// </remarks>
    public const string UnknownPolicy = "authorization.unknown-policy";

    /// <summary>
    ///     The fault code reported when a permission policy is evaluated with no permission lookup registered.
    /// </summary>
    public const string PermissionLookupMissing = "authorization.permission-lookup-missing";

    /// <summary>
    ///     The fault code reported when a policy names a condition type the dispatch scope did not register.
    /// </summary>
    public const string ConditionMissing = "authorization.condition-missing";

    /// <summary>
    ///     The fault code reported when a policy names a decision point type the dispatch scope did not register.
    /// </summary>
    public const string DecisionPointMissing = "authorization.decision-point-missing";

    /// <summary>
    ///     The fault code reported when the sign-in instant is missing or unparsable, so a recent sign-in cannot be
    ///     confirmed.
    /// </summary>
    public const string SignInInstantUnknown = "authorization.sign-in-instant-unknown";

    /// <summary>
    ///     The fault code reported when a recent sign-in policy resolves its window from a type the dispatch scope
    ///     did not register.
    /// </summary>
    public const string RecentSignInWindowMissing = "authorization.recent-sign-in-window-missing";

    /// <summary>
    ///     The denial code reported when the enclosing mediation does not handle the message type the policy
    ///     requires.
    /// </summary>
    public const string NotSentBy = "authorization.not-sent-by";
}
