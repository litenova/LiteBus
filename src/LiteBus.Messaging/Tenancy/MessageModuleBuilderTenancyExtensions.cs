using System;
using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Tenancy;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides tenancy registration for <see cref="MessageModuleBuilder" />.
/// </summary>
public static class MessageModuleBuilderTenancyExtensions
{
    /// <summary>
    ///     The tenancy options collected for each messaging module builder, so repeated calls configure one set.
    /// </summary>
    /// <remarks>
    ///     The collected validation cannot live on <see cref="MessageModuleBuilder" /> itself without editing that
    ///     type for every feature, so the table holds it beside the builder instead. One messaging module builder
    ///     serves one composition, so one entry covers every call, and the entry is dropped with the builder.
    /// </remarks>
    private static readonly ConditionalWeakTable<MessageModuleBuilder, TenancyOptions> TenancyOptionsByBuilder = new();

    /// <summary>
    ///     Registers the tenancy guard so every mediated message is checked against its declared tenancy position.
    /// </summary>
    /// <param name="builder">The messaging module builder.</param>
    /// <param name="configure">
    ///     Collects tenant validation on a <see cref="TenancyOptions" />, or <see langword="null" /> to accept every
    ///     non-blank tenant.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         The guard runs as an open generic, so one call covers every message: a message declaring a required
    ///         tenant is denied without one, and a message declaring a cross-tenant or tenantless position is allowed.
    ///         A message declaring no position is allowed, so an application adopts tenancy declaration by
    ///         declaration.
    ///     </para>
    ///     <para>
    ///         Pair this with <c>RequireDeclaration&lt;TenancyDeclaration&gt;()</c> so composition fails for a message
    ///         that states no position, rather than the omission passing the guard as an allowed message nobody
    ///         considered.
    ///     </para>
    ///     <para>
    ///         The call also registers the tenant identifier validator the guard, tenant policies, and tenant sources
    ///         read: the validation <paramref name="configure" /> collects, or the accepting LiteBus default when it
    ///         collects none. Tenant metadata resolves through the default provider, which answers every tenant with
    ///         unknown metadata, and the <c>litebus.tenancy.metadata</c> probe reports which provider answers.
    ///     </para>
    ///     <para>
    ///         The call is additive: a repeated call configures the same <see cref="TenancyOptions" />, so each
    ///         composition helper contributes validation from its own call. The guard, the registrations, and the
    ///         probe are registered once, on the first call. Repeating the same validator is a no-op, and a
    ///         different one fails composition naming both.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging
    ///     .UseTenancy(tenancy => tenancy
    ///         .ValidateTenant(tenantId => Guid.TryParse(tenantId, out _)))
    ///     .RequireDeclaration<TenancyDeclaration>());
    /// ]]></code>
    /// </example>
    public static MessageModuleBuilder UseTenancy(this MessageModuleBuilder builder, Action<TenancyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!TenancyOptionsByBuilder.TryGetValue(builder, out var options))
        {
            options = new TenancyOptions();
            TenancyOptionsByBuilder.Add(builder, options);

            builder.Register(typeof(TenancyGuard<>));
            builder.AddBuildStep(configuration => RegisterTenancy(configuration, options));
        }

        configure?.Invoke(options);

        return builder;
    }

    /// <summary>
    ///     Registers the configured tenant validator, the default tenant metadata provider, and the tenancy probe.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the registrations.</param>
    /// <param name="options">The tenancy options the composition collected.</param>
    /// <remarks>
    ///     The validator answers one question for the guard, tenant policies, and tenant sources, so one
    ///     implementation covers every path that accepts a tenant. A pre-created validator is shared by
    ///     construction; a validator type resolves per scope, which is what an implementation with dependencies
    ///     needs. The stateless defaults are singletons.
    /// </remarks>
    private static void RegisterTenancy(IModuleConfiguration configuration, TenancyOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ValidatorPredicate is { } predicate)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ITenantIdentifierValidator),
                new PredicateTenantIdentifierValidator(predicate)));
        }
        else if (options.ValidatorInstance is { } instance)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ITenantIdentifierValidator),
                instance));
        }
        else if (options.ValidatorType is { } validatorType)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ITenantIdentifierValidator),
                validatorType,
                InstanceLifetime.Transient));
        }
        else
        {
            configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
                typeof(ITenantIdentifierValidator),
                typeof(DefaultTenantIdentifierValidator),
                InstanceLifetime.Singleton));
        }

        configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
            typeof(ITenantMetadataProvider),
            typeof(DefaultTenantMetadataProvider),
            InstanceLifetime.Singleton));
        configuration.RegisterDiagnosticCheck(
            typeof(TenantMetadataDiagnosticCheck),
            TenantMetadataDiagnosticCheck.CheckName);
    }
}
