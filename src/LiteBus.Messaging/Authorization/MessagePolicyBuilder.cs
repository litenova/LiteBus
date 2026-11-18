using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Authorization;

/// <summary>
///     Collects the named authorization policies, contributed conditions, and permission lookup one composition registers.
/// </summary>
/// <remarks>
///     <para>
///         Reached through <c>MessageModuleBuilder.AddAuthorizationPolicies</c>. The builder only collects: the message
///         module reads it when it builds, registers the policy guard for every message, and publishes the collected
///         policies as a <see cref="MessagePolicyRegistry" /> singleton.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
///     .Add("vaults.open", MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Permission("vaults.open")))
///     .AddCondition<BusinessHoursCondition>()
///     .UsePermissionLookup<RolePermissionLookup>()));
/// ]]></code>
/// </example>
public sealed class MessagePolicyBuilder
{
    /// <summary>
    ///     The named policies collected so far, compared ordinally.
    /// </summary>
    private readonly Dictionary<string, MessagePolicy> _policies = new(StringComparer.Ordinal);

    /// <summary>
    ///     The condition implementation types collected so far, in registration order.
    /// </summary>
    private readonly List<Type> _conditionTypes = [];

    /// <summary>
    ///     The permission lookup implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    private Type? _permissionLookupType;

    /// <summary>
    ///     Gets the named policies collected so far.
    /// </summary>
    internal IReadOnlyDictionary<string, MessagePolicy> Policies => _policies;

    /// <summary>
    ///     Gets the condition implementation types collected so far, in registration order.
    /// </summary>
    internal IReadOnlyList<Type> ConditionTypes => _conditionTypes;

    /// <summary>
    ///     Gets the permission lookup implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    internal Type? PermissionLookupType => _permissionLookupType;

    /// <summary>
    ///     Registers a policy under the name messages declare.
    /// </summary>
    /// <param name="name">The name a message declares to select this policy, such as <c>vaults.open</c>.</param>
    /// <param name="policy">The policy evaluated for messages declaring <paramref name="name" />.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">A policy is already registered under <paramref name="name" />.</exception>
    /// <remarks>
    ///     Each name is registered once: two policies under one name would leave the guard guessing which one the
    ///     message meant, so the second registration fails composition naming the duplicate.
    /// </remarks>
    public MessagePolicyBuilder Add(string name, MessagePolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(policy);

        if (!_policies.TryAdd(name, policy))
        {
            throw new LiteBusConfigurationException(
                $"An authorization policy named '{name}' is already registered. Register each policy name once.");
        }

        return this;
    }

    /// <summary>
    ///     Registers a condition the policy guard runs for every authorized message before the named policy.
    /// </summary>
    /// <typeparam name="TCondition">The condition implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Conditions run in registration order and the first deny wins, so register the cheapest, most general
    ///     condition first. Registering the same condition type twice keeps one evaluation.
    /// </remarks>
    public MessagePolicyBuilder AddCondition<TCondition>()
        where TCondition : class, IAuthorizationCondition
    {
        _conditionTypes.Add(typeof(TCondition));

        return this;
    }

    /// <summary>
    ///     Registers the lookup permission policies read through.
    /// </summary>
    /// <typeparam name="TLookup">The lookup implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Only one lookup serves a composition; calling this twice keeps the last call. Without a lookup, a
    ///     permission policy denies with <c>authorization.permission-lookup-missing</c>.
    /// </remarks>
    public MessagePolicyBuilder UsePermissionLookup<TLookup>()
        where TLookup : class, IPermissionLookup
    {
        _permissionLookupType = typeof(TLookup);

        return this;
    }
}
