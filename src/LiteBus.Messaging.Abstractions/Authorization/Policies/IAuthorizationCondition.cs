using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     A check one feature contributes that runs for every authorized message before the named policy.
/// </summary>
/// <remarks>
///     <para>
///         Conditions are how a module adds its own authorization rule without owning the policy vocabulary. A tenancy
///         module denies a tenant-scoped message evaluated outside its tenant, a maintenance module denies writes while
///         a migration runs: each registers one condition, and the policy guard runs every registered condition, in
///         registration order, before the policy the message declares. The first deny wins, so a condition never
///         overrides another check that already refused. A fault ends the evaluation as a fault: a condition that
///         finds a port missing reports it instead of denying, because a configuration fault is never a user refusal.
///     </para>
///     <para>
///         A condition observes the same <see cref="AuthorizationContext" /> the named policy does. It must stay free of
///         effects a caller would not want from a "may I" question, because the guard stage also runs when the pipeline
///         is asked to evaluate a message without performing it.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class BusinessHoursCondition : IAuthorizationCondition
/// {
///     public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
///     {
///         var hour = context.HandledAt.Hour;
///
///         return new ValueTask<PolicyResult>(hour is >= 9 and < 17
///             ? PolicyResult.Allow
///             : PolicyResult.Deny(
///                 AuthorizationPolicyRefusalCodes.Forbidden,
///                 "Writes outside business hours need an on-call approval."));
///     }
/// }
/// ]]></code>
/// </example>
public interface IAuthorizationCondition
{
    /// <summary>
    ///     Decides whether the message is permitted to proceed.
    /// </summary>
    /// <param name="context">The message, identity, services, and handled instant the evaluation reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The result that allows, denies, or faults the message.</returns>
    ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken);
}
