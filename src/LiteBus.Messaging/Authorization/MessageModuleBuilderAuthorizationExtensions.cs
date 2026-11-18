using System;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Authorization;

/// <summary>
///     Provides the <see cref="MessageModuleBuilder" /> extension that registers composable authorization policies.
/// </summary>
public static class MessageModuleBuilderAuthorizationExtensions
{
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
    ///         the collected policies, conditions, and lookup are registered when the message module builds. Call this
    ///         once per composition: a second call fails composition because the policy set is already registered.
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
    public static MessageModuleBuilder AddAuthorizationPolicies(
        this MessageModuleBuilder builder,
        Action<MessagePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var policies = new MessagePolicyBuilder();
        configure(policies);

        builder.Register(typeof(PolicyAuthorizationGuard<>));

        return builder.AddBuildStep(configuration =>
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

            if (policies.PermissionLookupType is { } lookupType)
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IPermissionLookup),
                    lookupType));
            }
        });
    }
}
