using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Resolves the LiteBus execution identity from the current HTTP request principal.
/// </summary>
/// <remarks>
///     <para>
///         Reads <c>HttpContext.User</c> as the calling principal and the tenant from the first claim type named by
///         <see cref="ExecutionIdentityOptions.TenantClaimTypes" /> that carries a non-blank value. When no HTTP
///         context is available, the identity is <see cref="ExecutionIdentity.Anonymous" />.
///     </para>
///     <para>
///         The principal is passed through as the request carries it, including an unauthenticated principal, so
///         authorization policies observe the caller rather than a filtered copy. A principal is never persisted: only
///         the tenant travels onto durable envelopes.
///     </para>
///     <para>
///         The principal passes through unfiltered, including a transient principal the request carries only during
///         sign-in, such as the principal on an OpenID Connect callback path. A mediation started from such an
///         endpoint would attribute the work to that transient caller, so those endpoints pass an explicit identity
///         on the mediation settings instead.
///     </para>
///     <code><![CDATA[
/// await commandMediator.SendAsync(
///     command,
///     new CommandMediationSettings { Identity = ExecutionIdentity.Anonymous }).ConfigureAwait(false);
/// ]]></code>
/// </remarks>
public sealed class HttpContextExecutionIdentityProvider : IExecutionIdentityProvider
{
    /// <summary>
    ///     Provides the current HTTP context.
    /// </summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    ///     Names the claims that carry the tenant identifier, in the order they are tried.
    /// </summary>
    private readonly ExecutionIdentityOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HttpContextExecutionIdentityProvider" /> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides the current HTTP context.</param>
    /// <param name="options">Names the claims that carry the tenant identifier, in the order they are tried.</param>
    public HttpContextExecutionIdentityProvider(
        IHttpContextAccessor httpContextAccessor,
        ExecutionIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    /// <inheritdoc />
    public ExecutionIdentity GetCurrentIdentity()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return ExecutionIdentity.Anonymous;
        }

        string? tenantId = null;

        foreach (var claimType in _options.TenantClaimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;

            if (!string.IsNullOrWhiteSpace(value))
            {
                tenantId = value;
                break;
            }
        }

        return ExecutionIdentity.For(user, tenantId);
    }
}
