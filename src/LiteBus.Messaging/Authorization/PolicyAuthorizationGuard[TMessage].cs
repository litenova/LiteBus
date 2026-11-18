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
///         <see cref="AuthorizedDeclaration.Policy" />. The first deny wins. A name no composition registered denies
///         with <see cref="AuthorizationPolicyRefusalCodes.UnknownPolicy" /> rather than passing a message whose rule
///         is missing.
///     </para>
///     <para>
///         Exempt and undeclared messages never reach the policies: an
///         <see cref="AuthorizationExemptDeclaration" /> allows in the base guard, and a message declaring nothing
///         denies with <see cref="DeclarationGuard{TMessage, AuthorizationDeclaration}.UndeclaredCode" />.
///     </para>
/// </remarks>
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

        var context = new AuthorizationContext(
            message,
            message.GetType(),
            CurrentIdentity,
            _services,
            AmbientExecutionContext.GetCurrentOrDefault()?.HandledAt ?? DateTimeOffset.UtcNow);

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
            return Verdict.Deny(
                $"No authorization policy is registered under '{policy}'.",
                AuthorizationPolicyRefusalCodes.UnknownPolicy);
        }

        return await named.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
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
