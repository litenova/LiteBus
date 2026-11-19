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
    ///     The policy-scoped condition implementation types collected so far, in registration order.
    /// </summary>
    private readonly List<Type> _registeredConditionTypes = [];

    /// <summary>
    ///     The permission lookup implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    private Type? _permissionLookupType;

    /// <summary>
    ///     The sign-in instant source implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    private Type? _signInInstantSourceType;

    /// <summary>
    ///     Gets the named policies collected so far.
    /// </summary>
    internal IReadOnlyDictionary<string, MessagePolicy> Policies => _policies;

    /// <summary>
    ///     Gets the condition implementation types collected so far, in registration order.
    /// </summary>
    internal IReadOnlyList<Type> ConditionTypes => _conditionTypes;

    /// <summary>
    ///     Gets the policy-scoped condition implementation types collected so far, in registration order.
    /// </summary>
    internal IReadOnlyList<Type> RegisteredConditionTypes => _registeredConditionTypes;

    /// <summary>
    ///     Gets the permission lookup implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    internal Type? PermissionLookupType => _permissionLookupType;

    /// <summary>
    ///     Gets the sign-in instant source implementation type, or <see langword="null" /> when none was chosen.
    /// </summary>
    internal Type? SignInInstantSourceType => _signInInstantSourceType;

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
    ///     Registers a condition a policy names, resolved from the dispatch scope where the policy runs it.
    /// </summary>
    /// <typeparam name="TCondition">The condition implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The type is registered as itself, scoped, without joining the global conditions: it runs only where a
    ///     policy names it through <c>MessagePolicy.Condition</c>, unlike <c>AddCondition</c>, which runs for every
    ///     authorized message. Registering the same condition type twice keeps one registration.
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    ///     .Add("invoicing.sales-invoice-access", MessagePolicy.Condition<SalesInvoiceAccessCondition>())
    ///     .RegisterCondition<SalesInvoiceAccessCondition>()));
    /// ]]></code>
    /// </example>
    public MessagePolicyBuilder RegisterCondition<TCondition>()
        where TCondition : class, IAuthorizationCondition
    {
        var conditionType = typeof(TCondition);

        if (!_registeredConditionTypes.Contains(conditionType))
        {
            _registeredConditionTypes.Add(conditionType);
        }

        return this;
    }

    /// <summary>
    ///     Registers the lookup permission policies read through.
    /// </summary>
    /// <typeparam name="TLookup">The lookup implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <exception cref="LiteBusConfigurationException">A different lookup is already registered.</exception>
    /// <remarks>
    ///     Only one lookup serves a composition, so a second call naming another type fails composition; repeating
    ///     the same type is harmless, which is what repeated <c>AddAuthorizationPolicies</c> calls from several
    ///     assemblies produce. Without a lookup, a permission policy denies with
    ///     <see cref="AuthorizationPolicyRefusalCodes.PermissionLookupMissing" />.
    /// </remarks>
    public MessagePolicyBuilder UsePermissionLookup<TLookup>()
        where TLookup : class, IPermissionLookup
    {
        if (_permissionLookupType is not null && _permissionLookupType != typeof(TLookup))
        {
            throw new LiteBusConfigurationException(
                $"An authorization permission lookup '{_permissionLookupType.Name}' is already registered. Register "
                + $"one permission lookup per composition; '{typeof(TLookup).Name}' conflicts with it.");
        }

        _permissionLookupType = typeof(TLookup);

        return this;
    }

    /// <summary>
    ///     Registers the source recent sign-in policies read the sign-in instant through.
    /// </summary>
    /// <typeparam name="TSource">The source implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <exception cref="LiteBusConfigurationException">A different source is already registered.</exception>
    /// <remarks>
    ///     Only one source serves a composition, so a second call naming another type fails composition; repeating
    ///     the same type is harmless. Without a source, a recent sign-in policy reads the sign-in instant from the
    ///     <c>auth_time</c> claim instead.
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    ///     .Add("vaults.seal", MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(5)))
    ///     .UseSignInInstantSource<SessionSignInInstantSource>()));
    /// ]]></code>
    /// </example>
    public MessagePolicyBuilder UseSignInInstantSource<TSource>()
        where TSource : class, ISignInInstantSource
    {
        if (_signInInstantSourceType is not null && _signInInstantSourceType != typeof(TSource))
        {
            throw new LiteBusConfigurationException(
                $"An authorization sign-in instant source '{_signInInstantSourceType.Name}' is already registered. "
                + $"Register one sign-in instant source per composition; '{typeof(TSource).Name}' conflicts with it.");
        }

        _signInInstantSourceType = typeof(TSource);

        return this;
    }
}
