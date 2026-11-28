using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     Answers one authorization decision for an action on a message, resolved per policy from the dispatch scope.
/// </summary>
/// <remarks>
///     <para>
///         This is the general port a decision policy reads through. Relationship-based and attribute-based engines
///         plug in per policy behind it, each in its own opt-in adapter package that no core package references, so
///         a composition that answers permissions from roles pays nothing for the engines it does not run.
///         <see cref="IPermissionLookup" /> stays as the narrow string-permission convenience over the same idea.
///     </para>
///     <para>
///         The decision point answers the full three-outcome result: allow when the action is permitted, deny with a
///         caller-facing refusal when it is not, and fault when the decision cannot be made, such as when the backing
///         service is unreachable. The policy answers whatever the decision point returns.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class LedgerDecisionPoint : IAuthorizationDecisionPoint
/// {
///     public ValueTask<PolicyResult> DecideAsync(
///         AuthorizationDecisionRequest request,
///         CancellationToken cancellationToken)
///     {
///         if (request.Identity.Principal?.Identity?.IsAuthenticated is not true)
///         {
///             return new ValueTask<PolicyResult>(PolicyResult.Deny(
///                 AuthorizationPolicyRefusalCodes.NotSignedIn,
///                 "The action requires a signed-in caller."));
///         }
///
///         return new ValueTask<PolicyResult>(PolicyResult.Allow);
///     }
/// }
/// ]]></code>
/// </example>
public interface IAuthorizationDecisionPoint
{
    /// <summary>
    ///     Decides whether the requested action is permitted for the message being mediated.
    /// </summary>
    /// <param name="request">The subject, action, message, resource description, and handled instant the decision reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The decision: allow, deny, or fault when the decision cannot be made.</returns>
    ValueTask<PolicyResult> DecideAsync(AuthorizationDecisionRequest request, CancellationToken cancellationToken);
}
