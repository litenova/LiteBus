using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Decides whether an outbox-dispatched reaction that met a storage conflict publishes again.
/// </summary>
/// <remarks>
///     <para>
///         This is the port a lost storage race retries through. The dispatcher consults it only after a handler
///         failure the shared conflict classification recognizes, on both delivery paths: the background processor
///         pass and immediate dispatch after the commit. The decision is made per failure with the live failure,
///         attempt numbers, identity, clock, and dispatch scope, so one policy serves reactions whose backoff varies
///         by tenant, contract, or time of day, and a changed setting applies without rebuilding the composition.
///     </para>
///     <para>
///         The default policy retries with an exponential delay from
///         <c>OutboxReactionRetryOptions</c>. <c>UseReactionRetry</c> on the outbox module builder stays as the
///         simple overload over that constant curve. Implement this port when the bound or the delay must vary per
///         evaluation.
///     </para>
///     <para>
///         A policy never sees a business refusal or a configuration fault: the dispatcher ends those without
///         consulting it. Whatever the policy decides, the dispatcher enforces the configured per-dispatch bound,
///         so a policy that always retries cannot hold a lease forever.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class TenantReactionRetryPolicy : IOutboxReactionRetryPolicy
/// {
///     public ValueTask<OutboxReactionRetryDecision> DecideAsync(
///         OutboxReactionRetryContext context,
///         CancellationToken cancellationToken)
///     {
///         OutboxReactionRetryDecision decision = context.Evaluation.Identity.TenantId == "hot"
///             ? new OutboxReactionRetryDecision.Retry(TimeSpan.FromSeconds(2))
///             : new OutboxReactionRetryDecision.Retry(TimeSpan.FromMilliseconds(200));
///
///         return new ValueTask<OutboxReactionRetryDecision>(decision);
///     }
/// }
/// ]]></code>
/// </example>
public interface IOutboxReactionRetryPolicy
{
    /// <summary>
    ///     Decides whether the failed reaction publishes again.
    /// </summary>
    /// <param name="context">The failed attempt and the delivery it belongs to.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the dispatch operation.</param>
    /// <returns>The retry decision for this evaluation.</returns>
    ValueTask<OutboxReactionRetryDecision> DecideAsync(
        OutboxReactionRetryContext context,
        CancellationToken cancellationToken);
}
