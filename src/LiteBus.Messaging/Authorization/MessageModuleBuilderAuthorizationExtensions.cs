using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Authorization;

/// <summary>
///     Provides the <see cref="MessageModuleBuilder" /> extension that registers composable authorization policies.
/// </summary>
public static class MessageModuleBuilderAuthorizationExtensions
{
    /// <summary>
    ///     The policy builder collected for each messaging module builder, so repeated calls configure one set.
    /// </summary>
    /// <remarks>
    ///     The collected policies cannot live on <see cref="MessageModuleBuilder" /> itself without editing that type
    ///     for every feature, so the table holds them beside it instead. One messaging module builder serves one
    ///     composition, so one entry covers every call, and the entry is dropped with the builder.
    /// </remarks>
    private static readonly ConditionalWeakTable<MessageModuleBuilder, MessagePolicyBuilder> PolicyBuilders = new();

    /// <summary>
    ///     Registers the named authorization policies, contributed conditions, and permission lookup the policy guard evaluates.
    /// </summary>
    /// <param name="builder">The messaging module builder receiving the registrations.</param>
    /// <param name="configure">Collects the policies, conditions, and lookup on a <see cref="MessagePolicyBuilder" />.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         The guard is registered at configure time, so it covers messages registered later by the axis modules;
    ///         the collected policies, conditions, and lookup are registered when the message module builds.
    ///     </para>
    ///     <para>
    ///         The call is additive: a repeated call configures the same <see cref="MessagePolicyBuilder" />, so each
    ///         assembly in a modular monolith contributes its own policies, conditions, and lookup from its own call.
    ///         The guard, the build step, and the composition check are registered once, on the first call. A
    ///         duplicate policy name still fails, and a second permission lookup or sign-in instant source naming
    ///         another type fails.
    ///     </para>
    ///     <para>
    ///         Composition fails for a policy name a message declares that no call registered, naming every unknown
    ///         name and the messages that use it, so a typo fails once at startup instead of faulting every mediation.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    ///     .Add("vaults.open", MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Permission("vaults.open")))
    ///     .AddCondition<BusinessHoursCondition>()
    ///     .UsePermissionLookup<RolePermissionLookup>()));
    ///
    /// // A second call, from another assembly, adds to the same set rather than replacing it.
    /// registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(policies => policies
    ///     .Add("vaults.seal", MessagePolicy.Permission("vaults.seal"))));
    /// ]]></code>
    /// </example>
    public static MessageModuleBuilder AddAuthorizationPolicies(
        this MessageModuleBuilder builder,
        Action<MessagePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        if (PolicyBuilders.TryGetValue(builder, out var existing))
        {
            configure(existing);

            return builder;
        }

        var policies = new MessagePolicyBuilder();
        PolicyBuilders.Add(builder, policies);

        builder.Register(typeof(PolicyAuthorizationGuard<>));

        builder.ValidateComposition(ThrowOnUnknownPolicyNames);

        builder.AddBuildStep(configuration =>
        {
            ArgumentNullException.ThrowIfNull(configuration);

            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(MessagePolicyRegistry),
                new MessagePolicyRegistry(policies.Policies)));

            foreach (var conditionType in policies.ConditionTypes)
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(IAuthorizationCondition),
                    conditionType));
            }

            foreach (var conditionType in policies.RegisteredConditionTypes)
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    conditionType,
                    conditionType,
                    InstanceLifetime.Scoped));
            }

            if (policies.PermissionLookupType is { } lookupType)
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IPermissionLookup),
                    lookupType));
            }

            if (policies.SignInInstantSourceType is { } sourceType)
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(ISignInInstantSource),
                    sourceType));
            }
        });

        configure(policies);

        return builder;
    }

    /// <summary>
    ///     Fails composition when a message declares a policy name no <c>AddAuthorizationPolicies</c> call registered.
    /// </summary>
    /// <param name="model">Everything the host composed.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     One or more messages declare a policy name the composition never registered.
    /// </exception>
    /// <remarks>
    ///     A name checked here is a typo caught at startup. Left to mediation, the same typo raises
    ///     <see cref="MessageFaultException" /> carrying <see cref="AuthorizationPolicyRefusalCodes.UnknownPolicy" />
    ///     on every request instead of failing once where it was written. The registered names come from the model
    ///     rather than from the builder that collected them, so the check reads what the guard will read at mediation.
    /// </remarks>
    private static void ThrowOnUnknownPolicyNames(ICompositionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Dictionary<string, List<string>>? unknown = null;

        foreach (var entry in model.Messages)
        {
            if (!entry.Metadata.TryGet<AuthorizationDeclaration>(out var declaration)
                || declaration is not AuthorizedDeclaration authorized)
            {
                continue;
            }

            if (model.RegisteredPolicyNames.Contains(authorized.Policy))
            {
                continue;
            }

            unknown ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (!unknown.TryGetValue(authorized.Policy, out var messages))
            {
                messages = [];
                unknown[authorized.Policy] = messages;
            }

            messages.Add(entry.MessageType.Name);
        }

        if (unknown is null)
        {
            return;
        }

        var names = new List<string>(unknown.Keys);
        names.Sort(StringComparer.Ordinal);

        var lines = new List<string>(names.Count);

        foreach (var name in names)
        {
            var messages = unknown[name];
            messages.Sort(StringComparer.Ordinal);
            lines.Add($"  '{name}' is declared by: {string.Join(", ", messages)}");
        }

        throw new LiteBusConfigurationException(
            "One or more messages declare authorization policies that no AddAuthorizationPolicies call registered:"
            + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }
}
