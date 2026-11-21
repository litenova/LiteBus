using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     Resolves the sign-in window one recent sign-in policy evaluation accepts.
/// </summary>
/// <remarks>
///     <para>
///         This is the port a resolving recent sign-in policy reads through. The window is a business setting, so it
///         is resolved on every evaluation from the evaluation context: the tenant comes from the mediation identity,
///         and settings and stores resolve from the dispatch scope, so one policy serves a workspace with a
///         five-minute window and another with fifteen, and a changed setting applies without rebuilding the
///         composition.
///     </para>
///     <para>
///         <see cref="MessagePolicy.RecentSignIn(TimeSpan)" /> stays as the simple overload over a constant window.
///         Use this port when the window varies by tenant, time, or configuration.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class TenantSignInWindow : IRecentSignInWindow
/// {
///     public ValueTask<TimeSpan> GetWindowAsync(
///         AuthorizationContext context,
///         CancellationToken cancellationToken)
///     {
///         return new ValueTask<TimeSpan>(context.Identity.TenantId == "step-up"
///             ? TimeSpan.FromMinutes(5)
///             : TimeSpan.FromMinutes(30));
///     }
/// }
/// ]]></code>
/// </example>
public interface IRecentSignInWindow
{
    /// <summary>
    ///     Resolves the oldest sign-in the current evaluation accepts.
    /// </summary>
    /// <param name="context">The message, identity, services, and handled instant the evaluation reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The oldest sign-in the policy accepts for this evaluation.</returns>
    ValueTask<TimeSpan> GetWindowAsync(AuthorizationContext context, CancellationToken cancellationToken);
}
