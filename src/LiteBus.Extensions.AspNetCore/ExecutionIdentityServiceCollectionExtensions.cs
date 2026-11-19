using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Registers LiteBus execution identity resolution with the ASP.NET Core service collection.
/// </summary>
public static class ExecutionIdentityServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="HttpContextExecutionIdentityProvider" /> as the <see cref="IExecutionIdentityProvider" />
    ///     the mediator reads when a mediation carries no explicit identity.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">An optional callback that configures execution identity options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Registers the HTTP context accessor, the configured <see cref="ExecutionIdentityOptions" /> as a
    ///         singleton, and the provider as scoped, so each request resolves the identity of its own principal.
    ///     </para>
    ///     <para>
    ///         The mediator resolves the provider from the dispatch scope, which is a sibling of the request scope, so a
    ///         service the request scope holds reads as a fresh instance with no state inside the provider. This
    ///         registration bridges through <c>IHttpContextAccessor</c> instead: the provider reads the principal from
    ///         <c>HttpContext.User</c> and the tenant from the first claim type named by
    ///         <see cref="ExecutionIdentityOptions.TenantClaimTypes" /> that carries a non-blank value, both of which
    ///         stay available outside the request scope. An application that keeps the caller in its own request-scoped
    ///         holder stamps the same values onto claims or the HTTP context rather than reading the holder from the
    ///         provider.
    ///     </para>
    ///     <code><![CDATA[
    /// services.AddLiteBusExecutionIdentity(options =>
    /// {
    ///     options.TenantClaimTypes = ["session_tenant", "tenant_id"];
    /// });
    /// ]]></code>
    /// </remarks>
    public static IServiceCollection AddLiteBusExecutionIdentity(
        this IServiceCollection services,
        Action<ExecutionIdentityOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ExecutionIdentityOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddHttpContextAccessor();
        services.AddSingleton(options);
        services.AddScoped<IExecutionIdentityProvider, HttpContextExecutionIdentityProvider>();

        return services;
    }
}
