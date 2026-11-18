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
    ///     <code><![CDATA[
    /// services.AddLiteBusExecutionIdentity(options =>
    /// {
    ///     options.TenantClaimType = "tenant_id";
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
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TenantClaimType);

        services.AddHttpContextAccessor();
        services.AddSingleton(options);
        services.AddScoped<IExecutionIdentityProvider, HttpContextExecutionIdentityProvider>();

        return services;
    }
}
