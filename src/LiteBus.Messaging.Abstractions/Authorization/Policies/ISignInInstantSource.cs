using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     Reads the instant of the last sign-in for a recent sign-in policy evaluation.
/// </summary>
/// <remarks>
///     <para>
///         This is the port a recent sign-in policy reads through. Register one implementation when the sign-in
///         instant lives outside the token claims, such as in a server-side session record; with none registered,
///         the policy reads the <c>auth_time</c> claim instead.
///     </para>
///     <para>
///         A source that treats an unknown instant as allowed returns
///         <see cref="AuthorizationContext.HandledAt" />, which makes the sign-in age zero.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class SessionClaimSignInInstantSource : ISignInInstantSource
/// {
///     public ValueTask<DateTimeOffset?> GetSignInInstantAsync(
///         AuthorizationContext context,
///         CancellationToken cancellationToken)
///     {
///         var stamped = context.Identity.Principal?.FindFirst("session_start")?.Value;
///
///         return new ValueTask<DateTimeOffset?>(long.TryParse(stamped, out var unixSeconds)
///             ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
///             : null);
///     }
/// }
/// ]]></code>
/// </example>
public interface ISignInInstantSource
{
    /// <summary>
    ///     Reads the instant of the last sign-in for the principal being authorized.
    /// </summary>
    /// <param name="context">The message, identity, services, and handled instant the evaluation reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The sign-in instant, or <see langword="null" /> when it is unknown.</returns>
    ValueTask<DateTimeOffset?> GetSignInInstantAsync(AuthorizationContext context, CancellationToken cancellationToken);
}
