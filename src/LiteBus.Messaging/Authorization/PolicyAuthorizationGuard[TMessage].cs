using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;

namespace LiteBus.Messaging.Authorization;

/// <summary>
///     Guards a message by evaluating the registered authorization policy its declaration names.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <remarks>
///     <para>
///         Registered once as an open generic by <c>AddAuthorizationPolicies</c>, so it covers every message. A
///         message declaring <see cref="AuthorizedDeclaration" /> reaches one fixed evaluation order: every registered
///         <see cref="IAuthorizationCondition" /> in registration order, then the policy registered under
///         <see cref="AuthorizedDeclaration.Policy" />. The first deny wins. A name no composition registered is a
///         configuration fault, so the guard raises <see cref="MessageFaultException" /> carrying
///         <see cref="AuthorizationPolicyRefusalCodes.UnknownPolicy" /> rather than passing a message whose rule is
///         missing; composition normally fails on the name before any mediation runs.
///     </para>
///     <para>
///         The guard keeps the two-state <see cref="Verdict" /> at the pipeline boundary: an allow becomes
///         <see cref="Verdict.Allow" />, and a denial becomes a denial verdict carrying the same code and reason. A
///         fault is never a refusal, so it raises <see cref="MessageFaultException" /> carrying the fault code,
///         reason, and cause instead of returning a verdict.
///     </para>
///     <para>
///         Exempt and undeclared messages never reach the policies: an
///         <see cref="AuthorizationExemptDeclaration" /> allows in the base guard, and a message declaring nothing
///         denies with <see cref="DeclarationGuard{TMessage, AuthorizationDeclaration}.UndeclaredCode" />.
///     </para>
///     <para>
///         The evaluation context carries the ambient execution context, so a policy can read the enclosing
///         mediation the message arrived through.
///     </para>
///     <para>
///         The guard runs at <see cref="HandlerPriorities.Authorization" />, before the tenancy guard at
///         <see cref="HandlerPriorities.Tenancy" />, so a signed-out caller receives the authorization refusal
///         before the tenancy one. An application that wants the tenant checked before a permission puts a
///         tenant check inside its policy instead of reordering the guards.
///     </para>
/// </remarks>
[HandlerPriority(HandlerPriorities.Authorization)]
public sealed class PolicyAuthorizationGuard<TMessage> : AuthorizationGuard<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     The named policies the composition registered.
    /// </summary>
    private readonly MessagePolicyRegistry _registry;

    /// <summary>
    ///     The dispatch scope the evaluation resolves conditions and ports from.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PolicyAuthorizationGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="metadataAccessor">Reads the authorization declaration for the message being mediated.</param>
    /// <param name="registry">The named policies the composition registered.</param>
    /// <param name="services">The dispatch scope the evaluation resolves conditions and ports from.</param>
    public PolicyAuthorizationGuard(
        IMessageMetadataAccessor metadataAccessor,
        MessagePolicyRegistry registry,
        IServiceProvider services)
        : base(metadataAccessor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);

        _registry = registry;
        _services = services;
    }

    /// <inheritdoc />
    protected override async Task<Verdict> AuthorizeAsync(
        string policy,
        TMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(message);

        var execution = AmbientExecutionContext.GetCurrentOrDefault();

        // The messaging module registers the mediation clock in the dispatch scope, so the guard reads it from
        // there rather than defaulting to the system clock: a policy measuring a window must read the same clock
        // the mediation ran on. A scope built by hand without one keeps the system clock.
        var clock = _services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

        var context = new AuthorizationContext(
            message,
            message.GetType(),
            CurrentIdentity,
            _services,
            clock,
            execution?.HandledAt ?? clock.GetUtcNow())
        {
            ExecutionContext = execution
        };

        foreach (var condition in ResolveConditions())
        {
            var conditionVerdict = await condition.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);

            if (conditionVerdict.IsDenied)
            {
                return conditionVerdict;
            }
        }

        if (!_registry.TryGetPolicy(policy, out var named))
        {
            throw new MessageFaultException(
                context.MessageType,
                AuthorizationPolicyRefusalCodes.UnknownPolicy,
                $"No authorization policy is registered under '{policy}'.");
        }

        var result = await named.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);

        if (result is PolicyAllow)
        {
            return Verdict.Allow;
        }

        if (result is PolicyDeny deny)
        {
            return Verdict.Deny(deny.Reason, deny.Code);
        }

        var fault = (PolicyFault) result;

        throw fault.Cause is null
            ? new MessageFaultException(context.MessageType, fault.Code, fault.Reason)
            : new MessageFaultException(context.MessageType, fault.Code, fault.Reason, fault.Cause);
    }

    /// <summary>
    ///     Resolves the registered authorization conditions from the dispatch scope.
    /// </summary>
    /// <returns>The registered conditions in registration order, or an empty list when there are none.</returns>
    /// <remarks>
    ///     The collection resolves first, because that is how the messaging module registers conditions. A single
    ///     condition registered directly against the service type is honored as a one-item collection, so a host
    ///     that wires one by hand without the module builder still contributes.
    /// </remarks>
    private IAuthorizationCondition[] ResolveConditions()
    {
        if (_services.GetService(typeof(IEnumerable<IAuthorizationCondition>)) is IEnumerable<IAuthorizationCondition> conditions)
        {
            return conditions.ToArray();
        }

        if (_services.GetService(typeof(IAuthorizationCondition)) is IAuthorizationCondition condition)
        {
            return [condition];
        }

        return [];
    }
}
