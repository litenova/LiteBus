using System;
using LiteBus.Messaging.Tenancy;

namespace LiteBus.Messaging;

/// <summary>
///     Provides tenancy registration for <see cref="MessageModuleBuilder" />.
/// </summary>
public static class MessageModuleBuilderTenancyExtensions
{
    /// <summary>
    ///     Registers the tenancy guard so every mediated message is checked against its declared tenancy position.
    /// </summary>
    /// <param name="builder">The messaging module builder.</param>
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
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging
    ///     .UseTenancy()
    ///     .RequireDeclaration<TenancyDeclaration>());
    /// ]]></code>
    /// </example>
    public static MessageModuleBuilder UseTenancy(this MessageModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Register(typeof(TenancyGuard<>));

        return builder;
    }
}
