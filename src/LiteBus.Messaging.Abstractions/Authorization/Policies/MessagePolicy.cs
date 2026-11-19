using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     A named authorization rule a message declares through <see cref="AuthorizedDeclaration" /> and the policy guard evaluates.
/// </summary>
/// <remarks>
///     <para>
///         Before this model, each product wrote its own policy engine: signed-in, permission, self, recent sign-in,
///         system, and the conditions modules contribute. Those checks are the same everywhere, so they live here once,
///         as static combinators over one evaluation shape. A composition registers named policies, a message declares
///         one name, and the guard evaluates it with one fixed order: every registered
///         <see cref="IAuthorizationCondition" /> in registration order, then the named policy, with the first deny
///         winning.
///     </para>
///     <para>
///         A check the combinators cannot express is a subclass: derive from this record and override
///         <see cref="EvaluateAsync(AuthorizationContext, CancellationToken)" />. The subclass must itself be a
///         record, because only records may inherit from a record. The guard treats a custom policy exactly like a
///         built one, so the composition vocabulary and the evaluation order stay in one place whatever the checks
///         are.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// policies.Add("vaults.open", MessagePolicy.All(
///     MessagePolicy.SignedIn(),
///     MessagePolicy.Permission("vaults.open")));
/// ]]></code>
/// </example>
public abstract record MessagePolicy
{
    /// <summary>
    ///     The claim type that carries the instant of the last sign-in, in Unix seconds.
    /// </summary>
    /// <remarks>
    ///     The OpenID Connect <c>auth_time</c> claim. Identity providers that authenticate interactively stamp it; a
    ///     principal without it never signed in through such a flow, so a recent sign-in cannot be confirmed.
    /// </remarks>
    private const string AuthTimeClaimType = "auth_time";

    /// <summary>
    ///     The fallback claim type that identifies the principal when no framework identifier claim is present.
    /// </summary>
    /// <remarks>
    ///     The OpenID Connect subject claim, which is what an identity provider stamps when the caller authenticated
    ///     outside the framework identity that carries <c>ClaimTypes.NameIdentifier</c>.
    /// </remarks>
    private const string SubjectClaimType = "sub";

    /// <summary>
    ///     Decides whether the message is permitted to proceed.
    /// </summary>
    /// <param name="context">The message, identity, services, and handled instant the evaluation reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    public abstract ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken);

    /// <summary>
    ///     Creates a policy that allows a signed-in caller and denies anyone else.
    /// </summary>
    /// <returns>The signed-in policy.</returns>
    /// <remarks>
    ///     Signed in means the mediation identity carries an authenticated principal. A denial carries
    ///     <see cref="AuthorizationPolicyRefusalCodes.NotSignedIn" />, so a boundary can answer it with an
    ///     authentication challenge rather than a refusal.
    /// </remarks>
    public static MessagePolicy SignedIn()
    {
        return new SignedInPolicy();
    }

    /// <summary>
    ///     Creates a policy that allows when the mediation runs in a tenant.
    /// </summary>
    /// <returns>The tenant policy.</returns>
    /// <remarks>
    ///     Allows when the mediation identity carries a tenant; otherwise denies with
    ///     <see cref="TenancyRefusalCodes.TenantRequired" />. It lets an application order sign-in, tenant, and
    ///     permission checks inside one policy, as in
    ///     <c>All(SignedIn(), Tenant(), Permission("vaults.open"))</c>.
    /// </remarks>
    public static MessagePolicy Tenant()
    {
        return new TenantPolicy();
    }

    /// <summary>
    ///     Creates a policy that allows when the caller holds a permission.
    /// </summary>
    /// <param name="permission">The permission the caller must hold, in the application's own vocabulary.</param>
    /// <returns>The permission policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="permission" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     The check runs through the registered <see cref="IPermissionLookup" />. With none registered, the policy
    ///     denies with <see cref="AuthorizationPolicyRefusalCodes.PermissionLookupMissing" /> rather than guessing; a
    ///     lookup that answers <see langword="false" /> denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.MissingPermission" />.
    /// </remarks>
    public static MessagePolicy Permission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return new PermissionPolicy(permission);
    }

    /// <summary>
    ///     Creates a policy that allows when the principal carries a claim.
    /// </summary>
    /// <param name="type">The claim type the principal must carry.</param>
    /// <param name="value">The claim value required, or <see langword="null" /> to accept any value.</param>
    /// <returns>The claim policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="type" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     A caller that is not signed in denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.NotSignedIn" />; a signed-in caller without the claim denies
    ///     with <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />. Type and value compare ordinally.
    /// </remarks>
    public static MessagePolicy Claim(string type, string? value = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        return new ClaimPolicy(type, value);
    }

    /// <summary>
    ///     Creates a policy that allows when the last sign-in is recent.
    /// </summary>
    /// <param name="maxAge">The oldest sign-in the policy accepts.</param>
    /// <returns>The recent sign-in policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge" /> is negative.</exception>
    /// <remarks>
    ///     <para>
    ///         Reads the sign-in instant from the registered <see cref="ISignInInstantSource" /> when one is present,
    ///         else from the <c>auth_time</c> claim, in Unix seconds, and compares its age against the handled instant
    ///         of the mediation as a <see cref="TimeSpan" />. A caller that is not signed in denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.NotSignedIn" />; a signed-in caller whose sign-in is older
    ///         than <paramref name="maxAge" /> denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />; a signed-in caller with a missing or
    ///         unparsable sign-in instant denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.SignInInstantUnknown" />.
    ///     </para>
    /// </remarks>
    public static MessagePolicy RecentSignIn(TimeSpan maxAge)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);

        return new RecentSignInPolicy(maxAge, AuthTimeClaimType);
    }

    /// <summary>
    ///     Creates a policy that allows when the last sign-in is recent, reading the sign-in instant from a chosen
    ///     claim.
    /// </summary>
    /// <param name="maxAge">The oldest sign-in the policy accepts.</param>
    /// <param name="claimType">The claim type that carries the sign-in instant, in Unix seconds.</param>
    /// <returns>The recent sign-in policy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge" /> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="claimType" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     <para>
    ///         Reads the sign-in instant from the registered <see cref="ISignInInstantSource" /> when one is present,
    ///         else from the <paramref name="claimType" /> claim, in Unix seconds, and compares its age against the
    ///         handled instant of the mediation as a <see cref="TimeSpan" />. A caller that is not signed in denies
    ///         with <see cref="AuthorizationPolicyRefusalCodes.NotSignedIn" />; a signed-in caller whose sign-in is
    ///         older than <paramref name="maxAge" /> denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />; a signed-in caller with a missing or
    ///         unparsable sign-in instant denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.SignInInstantUnknown" />.
    ///     </para>
    /// </remarks>
    public static MessagePolicy RecentSignIn(TimeSpan maxAge, string claimType)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimType);

        return new RecentSignInPolicy(maxAge, claimType);
    }

    /// <summary>
    ///     Creates a policy that allows a system run and denies any call with a principal.
    /// </summary>
    /// <returns>The system policy.</returns>
    /// <remarks>
    ///     A system run is a mediation whose identity carries no principal, such as a background reaction or a
    ///     recurring job. A call with a principal denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />.
    /// </remarks>
    public static MessagePolicy System()
    {
        return new SystemPolicy();
    }

    /// <summary>
    ///     Creates a policy that allows when the caller is the subject the message names.
    /// </summary>
    /// <typeparam name="TMessage">The message type the subject selector reads.</typeparam>
    /// <param name="subjectId">Reads the owning subject identifier from the message.</param>
    /// <returns>The ownership policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="subjectId" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         The principal's identifier is its <c>ClaimTypes.NameIdentifier</c> claim, else its <c>sub</c> claim,
    ///         which is the same order the audit actor resolution reads. The identifiers compare ordinally.
    ///     </para>
    ///     <para>
    ///         A caller that is not signed in denies with
    ///         <see cref="AuthorizationPolicyRefusalCodes.NotSignedIn" />. A message of another type, a message
    ///         naming no subject, a principal carrying no identifier, and an identifier that differs all deny with
    ///         <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />: ownership that cannot be confirmed is refused
    ///         rather than guessed.
    ///     </para>
    /// </remarks>
    public static MessagePolicy Self<TMessage>(Func<TMessage, string?> subjectId)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(subjectId);

        return new SelfPolicy<TMessage>(subjectId);
    }

    /// <summary>
    ///     Creates a policy that evaluates one authorization condition where the policy names it.
    /// </summary>
    /// <typeparam name="TCondition">The condition type resolved from the dispatch scope.</typeparam>
    /// <returns>The condition policy.</returns>
    /// <remarks>
    ///     Resolves <typeparamref name="TCondition" /> from the dispatch scope and answers its verdict, so the check
    ///     runs only where a policy names it. A condition registered with the composition runs for every authorized
    ///     message; this combinator names one condition for one policy. When the condition type is not registered,
    ///     the policy denies with <see cref="AuthorizationPolicyRefusalCodes.ConditionMissing" /> rather than
    ///     guessing.
    /// </remarks>
    public static MessagePolicy Condition<TCondition>()
        where TCondition : class, IAuthorizationCondition
    {
        return new ConditionPolicy<TCondition>();
    }

    /// <summary>
    ///     Creates a policy that allows when the enclosing mediation handles a named message.
    /// </summary>
    /// <typeparam name="TParent">The message type the enclosing mediation must handle.</typeparam>
    /// <returns>The sent-by policy.</returns>
    /// <remarks>
    ///     Allows when the parent execution context carries a message type assignable to
    ///     <typeparamref name="TParent" />; otherwise denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.NotSentBy" />. A root mediation, with no enclosing mediation,
    ///     denies. Combine it with a permission check to admit either a grant or a named caller, as in
    ///     <c>Any(Permission("vaults.open"), SentBy&lt;ExportArchiveQuery&gt;())</c>.
    /// </remarks>
    public static MessagePolicy SentBy<TParent>()
    {
        return new SentByPolicy<TParent>();
    }

    /// <summary>
    ///     Creates a policy that allows when every inner policy allows.
    /// </summary>
    /// <param name="policies">The inner policies, evaluated in order.</param>
    /// <returns>The conjunctive policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policies" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="policies" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The first deny wins and evaluation stops there, so a later policy never overrides an earlier refusal. An
    ///     empty conjunction allows.
    /// </remarks>
    public static MessagePolicy All(params MessagePolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        return new AllPolicy(Copy(policies, nameof(All)));
    }

    /// <summary>
    ///     Creates a policy that allows when any inner policy allows.
    /// </summary>
    /// <param name="policies">The inner policies, evaluated in order.</param>
    /// <returns>The disjunctive policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policies" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">One of <paramref name="policies" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The first allow wins and evaluation stops there. When nothing allows, the first deny is returned, so the
    ///     refusal names the earliest requirement the caller missed. An empty disjunction denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />.
    /// </remarks>
    public static MessagePolicy Any(params MessagePolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        return new AnyPolicy(Copy(policies, nameof(Any)));
    }

    /// <summary>
    ///     Creates a policy that applies an inner policy only when a condition holds.
    /// </summary>
    /// <param name="condition">Decides whether the inner policy applies to the message being mediated.</param>
    /// <param name="inner">The policy evaluated when <paramref name="condition" /> holds.</param>
    /// <returns>The conditional policy.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="condition" /> or <paramref name="inner" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     When the condition does not hold, the policy allows without evaluating the inner policy, because a rule
    ///     that does not apply must not refuse.
    /// </remarks>
    public static MessagePolicy When(Func<AuthorizationContext, bool> condition, MessagePolicy inner)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(inner);

        return new WhenPolicy(condition, inner);
    }

    /// <summary>
    ///     Creates a policy that always allows.
    /// </summary>
    /// <returns>The allowing policy.</returns>
    /// <remarks>
    ///     The branch of a <see cref="When" /> that needs no check, and the placeholder a composition holds until the
    ///     real rule is written.
    /// </remarks>
    public static MessagePolicy Allow()
    {
        return new AllowPolicy();
    }

    /// <summary>
    ///     Creates a policy that always allows, for routes that need no caller.
    /// </summary>
    /// <returns>The public policy.</returns>
    /// <remarks>
    ///     The permanent policy for public routes such as sign-in start and token refresh, where there is no caller
    ///     to authorize yet. Unlike <see cref="Allow" />, which is the placeholder a composition holds until the real
    ///     rule is written, this policy states that the message is public by design.
    /// </remarks>
    public static MessagePolicy Public()
    {
        return new PublicPolicy();
    }

    /// <summary>
    ///     Creates a policy that always denies with a reason.
    /// </summary>
    /// <param name="reason">Why the message is denied, written for a person.</param>
    /// <returns>The denying policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     The denial carries <see cref="AuthorizationPolicyRefusalCodes.Forbidden" />. It is how a composition
    ///     closes a message while it states, in its own words, what to do instead.
    /// </remarks>
    public static MessagePolicy Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new DenyPolicy(reason);
    }

    /// <summary>
    ///     Wraps this policy so a denial carries the given code and reason.
    /// </summary>
    /// <param name="code">The refusal code the denial carries.</param>
    /// <param name="reason">The denial reason, or <see langword="null" /> to keep the inner reason.</param>
    /// <returns>The wrapping policy.</returns>
    /// <exception cref="ArgumentException"><paramref name="code" /> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is empty or whitespace.</exception>
    /// <remarks>
    ///     An allow passes through unchanged; a denial keeps its decision and carries <paramref name="code" /> with
    ///     <paramref name="reason" /> when one is given, else the inner reason. It lets an application keep its own
    ///     refusal codes on built-in combinators.
    /// </remarks>
    public MessagePolicy WithRefusal(string code, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (reason is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        }

        return new WithRefusalPolicy(this, code, reason);
    }

    /// <summary>
    ///     Determines whether an identity carries an authenticated principal.
    /// </summary>
    /// <param name="identity">The mediation identity to inspect.</param>
    /// <returns><see langword="true" /> when a signed-in caller is present.</returns>
    /// <remarks>
    ///     An anonymous request still carries a principal object with an unauthenticated identity, so presence alone
    ///     cannot tell a caller apart from its absence. The authentication flag is the line.
    /// </remarks>
    private static bool IsSignedIn(ExecutionIdentity identity)
    {
        return identity.Principal?.Identity?.IsAuthenticated is true;
    }

    /// <summary>
    ///     Copies a combinator argument list after rejecting a null element.
    /// </summary>
    /// <param name="policies">The inner policies the caller passed.</param>
    /// <param name="factory">The factory name the error message names.</param>
    /// <returns>A copy of the inner policies.</returns>
    /// <exception cref="ArgumentException">One of <paramref name="policies" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A null element would otherwise fail at evaluation with no composition line to blame. The copy stops a
    ///     caller-held array from changing the policy after it is built.
    /// </remarks>
    private static MessagePolicy[] Copy(MessagePolicy[] policies, string factory)
    {
        foreach (var policy in policies)
        {
            if (policy is null)
            {
                throw new ArgumentException(
                    $"Every policy passed to '{factory}' must be non-null.",
                    nameof(policies));
            }
        }

        return [.. policies];
    }

    /// <summary>
    ///     Reads the identifier of a principal for an ownership check.
    /// </summary>
    /// <param name="principal">The signed-in principal.</param>
    /// <returns>The framework identifier claim, else the subject claim, else <see langword="null" />.</returns>
    private static string? PrincipalId(ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(SubjectClaimType)?.Value;
    }

    /// <summary>
    ///     Reads the sign-in instant a claim carries, in Unix seconds.
    /// </summary>
    /// <param name="principal">The signed-in principal.</param>
    /// <param name="claimType">The claim type that carries the sign-in instant.</param>
    /// <returns>The sign-in instant, or <see langword="null" /> when the claim is missing or unusable.</returns>
    /// <remarks>
    ///     A value outside the representable instant range is unusable like a missing one: no sign-in can be
    ///     confirmed from it.
    /// </remarks>
    private static DateTimeOffset? ClaimInstant(ClaimsPrincipal principal, string claimType)
    {
        var stamped = principal.FindFirst(claimType)?.Value;

        if (stamped is null
            || !long.TryParse(stamped, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    ///     The policy <see cref="SignedIn" /> creates.
    /// </summary>
    private sealed record SignedInPolicy : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(IsSignedIn(context.Identity)
                ? Verdict.Allow
                : Verdict.Deny(
                    "The message requires a signed-in caller.",
                    AuthorizationPolicyRefusalCodes.NotSignedIn));
        }
    }

    /// <summary>
    ///     The policy <see cref="Tenant" /> creates.
    /// </summary>
    private sealed record TenantPolicy : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(context.Identity.HasTenant
                ? Verdict.Allow
                : Verdict.Deny(
                    $"The message '{context.MessageType.Name}' requires a tenant, but the mediation runs in none.",
                    TenancyRefusalCodes.TenantRequired));
        }
    }

    /// <summary>
    ///     The policy <see cref="Permission" /> creates.
    /// </summary>
    /// <param name="RequiredPermission">The permission the caller must hold.</param>
    private sealed record PermissionPolicy(string RequiredPermission) : MessagePolicy
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Services.GetService(typeof(IPermissionLookup)) is not IPermissionLookup lookup)
            {
                return Verdict.Deny(
                    $"No permission lookup is registered, so the '{RequiredPermission}' permission cannot be granted.",
                    AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
            }

            var holds = await lookup
                .HasPermissionAsync(context.Identity, RequiredPermission, context, cancellationToken)
                .ConfigureAwait(false);

            return holds
                ? Verdict.Allow
                : Verdict.Deny(
                    $"The caller does not hold the '{RequiredPermission}' permission.",
                    AuthorizationPolicyRefusalCodes.MissingPermission);
        }
    }

    /// <summary>
    ///     The policy <see cref="Claim" /> creates.
    /// </summary>
    /// <param name="Type">The claim type the principal must carry.</param>
    /// <param name="Value">The claim value required, or <see langword="null" /> to accept any value.</param>
    private sealed record ClaimPolicy(string Type, string? Value) : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Identity.Principal is not { } principal || !IsSignedIn(context.Identity))
            {
                return new ValueTask<Verdict>(Verdict.Deny(
                    "The message requires a signed-in caller.",
                    AuthorizationPolicyRefusalCodes.NotSignedIn));
            }

            var carries = Value is null
                ? principal.HasClaim(claim => string.Equals(claim.Type, Type, StringComparison.Ordinal))
                : principal.HasClaim(claim =>
                    string.Equals(claim.Type, Type, StringComparison.Ordinal)
                    && string.Equals(claim.Value, Value, StringComparison.Ordinal));

            if (carries)
            {
                return new ValueTask<Verdict>(Verdict.Allow);
            }

            var reason = Value is null
                ? $"The caller does not carry a '{Type}' claim."
                : $"The caller does not carry a '{Type}' claim with the required value.";

            return new ValueTask<Verdict>(Verdict.Deny(reason, AuthorizationPolicyRefusalCodes.Forbidden));
        }
    }

    /// <summary>
    ///     The policy that <see cref="RecentSignIn(TimeSpan)" /> and <see cref="RecentSignIn(TimeSpan, string)" /> create.
    /// </summary>
    /// <param name="MaxAge">The oldest sign-in the policy accepts.</param>
    /// <param name="ClaimType">The claim type that carries the sign-in instant when no source is registered.</param>
    private sealed record RecentSignInPolicy(TimeSpan MaxAge, string ClaimType) : MessagePolicy
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Identity.Principal is not { } principal || !IsSignedIn(context.Identity))
            {
                return Verdict.Deny(
                    "The message requires a signed-in caller.",
                    AuthorizationPolicyRefusalCodes.NotSignedIn);
            }

            DateTimeOffset? signedInAt;

            if (context.Services.GetService(typeof(ISignInInstantSource)) is ISignInInstantSource source)
            {
                signedInAt = await source
                    .GetSignInInstantAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                signedInAt = ClaimInstant(principal, ClaimType);
            }

            if (signedInAt is null)
            {
                return Verdict.Deny(
                    "No usable sign-in instant is available, so a recent sign-in cannot be confirmed.",
                    AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
            }

            if (context.HandledAt - signedInAt.Value > MaxAge)
            {
                return Verdict.Deny(
                    $"The sign-in is older than the allowed age of {MaxAge}.",
                    AuthorizationPolicyRefusalCodes.Forbidden);
            }

            return Verdict.Allow;
        }
    }

    /// <summary>
    ///     The policy <see cref="System" /> creates.
    /// </summary>
    private sealed record SystemPolicy : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(context.Identity.Principal is null
                ? Verdict.Allow
                : Verdict.Deny(
                    "The policy allows only a system run with no calling principal.",
                    AuthorizationPolicyRefusalCodes.Forbidden));
        }
    }

    /// <summary>
    ///     The policy <see cref="Self{TMessage}" /> creates.
    /// </summary>
    /// <typeparam name="TMessage">The message type the subject selector reads.</typeparam>
    /// <param name="SubjectId">Reads the owning subject identifier from the message.</param>
    private sealed record SelfPolicy<TMessage>(Func<TMessage, string?> SubjectId) : MessagePolicy
        where TMessage : notnull
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Identity.Principal is not { } principal || !IsSignedIn(context.Identity))
            {
                return new ValueTask<Verdict>(Verdict.Deny(
                    "The message requires a signed-in caller.",
                    AuthorizationPolicyRefusalCodes.NotSignedIn));
            }

            if (context.Message is not TMessage message)
            {
                return new ValueTask<Verdict>(Verdict.Deny(
                    $"The policy applies to '{typeof(TMessage).Name}', but the message is '{context.MessageType.Name}'.",
                    AuthorizationPolicyRefusalCodes.Forbidden));
            }

            if (SubjectId(message) is not { } subject)
            {
                return new ValueTask<Verdict>(Verdict.Deny(
                    "The message names no subject, so ownership cannot be confirmed.",
                    AuthorizationPolicyRefusalCodes.Forbidden));
            }

            if (PrincipalId(principal) is not { } caller)
            {
                return new ValueTask<Verdict>(Verdict.Deny(
                    "The principal carries no identifier claim, so ownership cannot be confirmed.",
                    AuthorizationPolicyRefusalCodes.Forbidden));
            }

            return new ValueTask<Verdict>(string.Equals(caller, subject, StringComparison.Ordinal)
                ? Verdict.Allow
                : Verdict.Deny(
                    "The caller is not the subject the message names.",
                    AuthorizationPolicyRefusalCodes.Forbidden));
        }
    }

    /// <summary>
    ///     The policy <see cref="Condition{TCondition}" /> creates.
    /// </summary>
    /// <typeparam name="TCondition">The condition type resolved from the dispatch scope.</typeparam>
    private sealed record ConditionPolicy<TCondition> : MessagePolicy
        where TCondition : class, IAuthorizationCondition
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Services.GetService(typeof(TCondition)) is not TCondition condition)
            {
                return Verdict.Deny(
                    $"No '{typeof(TCondition).Name}' condition is registered, so the policy cannot be evaluated.",
                    AuthorizationPolicyRefusalCodes.ConditionMissing);
            }

            return await condition.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The policy <see cref="SentBy{TParent}" /> creates.
    /// </summary>
    /// <typeparam name="TParent">The message type the enclosing mediation must handle.</typeparam>
    private sealed record SentByPolicy<TParent> : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.ExecutionContext?.Parent?.MessageType is { } parentMessageType
                && typeof(TParent).IsAssignableFrom(parentMessageType))
            {
                return new ValueTask<Verdict>(Verdict.Allow);
            }

            return new ValueTask<Verdict>(Verdict.Deny(
                $"The message was not sent by '{typeof(TParent).Name}'.",
                AuthorizationPolicyRefusalCodes.NotSentBy));
        }
    }

    /// <summary>
    ///     The policy <see cref="All" /> creates.
    /// </summary>
    /// <param name="Policies">The inner policies, evaluated in order.</param>
    private sealed record AllPolicy(MessagePolicy[] Policies) : MessagePolicy
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            foreach (var policy in Policies)
            {
                var verdict = await policy.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);

                if (verdict.IsDenied)
                {
                    return verdict;
                }
            }

            return Verdict.Allow;
        }
    }

    /// <summary>
    ///     The policy <see cref="Any" /> creates.
    /// </summary>
    /// <param name="Policies">The inner policies, evaluated in order.</param>
    private sealed record AnyPolicy(MessagePolicy[] Policies) : MessagePolicy
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            Verdict? firstDenial = null;

            foreach (var policy in Policies)
            {
                var verdict = await policy.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);

                if (!verdict.IsDenied)
                {
                    return Verdict.Allow;
                }

                firstDenial ??= verdict;
            }

            return firstDenial
                ?? Verdict.Deny("No policy allowed the message.", AuthorizationPolicyRefusalCodes.Forbidden);
        }
    }

    /// <summary>
    ///     The policy <see cref="When" /> creates.
    /// </summary>
    /// <param name="Condition">Decides whether the inner policy applies to the message being mediated.</param>
    /// <param name="Inner">The policy evaluated when <paramref name="Condition" /> holds.</param>
    private sealed record WhenPolicy(Func<AuthorizationContext, bool> Condition, MessagePolicy Inner) : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return Condition(context)
                ? Inner.EvaluateAsync(context, cancellationToken)
                : new ValueTask<Verdict>(Verdict.Allow);
        }
    }

    /// <summary>
    ///     The policy <see cref="Allow" /> creates.
    /// </summary>
    private sealed record AllowPolicy : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(Verdict.Allow);
        }
    }

    /// <summary>
    ///     The policy <see cref="Public" /> creates.
    /// </summary>
    private sealed record PublicPolicy : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(Verdict.Allow);
        }
    }

    /// <summary>
    ///     The policy <see cref="Deny" /> creates.
    /// </summary>
    /// <param name="Reason">Why the message is denied, written for a person.</param>
    private sealed record DenyPolicy(string Reason) : MessagePolicy
    {
        /// <inheritdoc />
        public override ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<Verdict>(Verdict.Deny(Reason, AuthorizationPolicyRefusalCodes.Forbidden));
        }
    }

    /// <summary>
    ///     The policy <see cref="WithRefusal" /> creates.
    /// </summary>
    /// <param name="Inner">The wrapped policy.</param>
    /// <param name="Code">The refusal code a denial carries.</param>
    /// <param name="Reason">The denial reason, or <see langword="null" /> to keep the inner reason.</param>
    private sealed record WithRefusalPolicy(MessagePolicy Inner, string Code, string? Reason) : MessagePolicy
    {
        /// <inheritdoc />
        public override async ValueTask<Verdict> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            var verdict = await Inner.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);

            if (!verdict.IsDenied)
            {
                return verdict;
            }

            return Verdict.Deny(Reason ?? verdict.Reason ?? Code, Code);
        }
    }
}
