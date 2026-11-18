using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Resolves the LiteBus execution identity from the current HTTP request principal.
/// </summary>
/// <remarks>
///     <para>
///         Reads <c>HttpContext.User</c> as the calling principal and the tenant from the claim named by
///         <see cref="ExecutionIdentityOptions.TenantClaimType" />. When no HTTP context is available, the identity is
///         <see cref="ExecutionIdentity.Anonymous" />.
///     </para>
///     <para>
///         The principal is passed through as the request carries it, including an unauthenticated principal, so
///         authorization policies observe the caller rather than a filtered copy. A principal is never persisted: only
///         the tenant travels onto durable envelopes.
///     </para>
/// </remarks>
public sealed class HttpContextExecutionIdentityProvider : IExecutionIdentityProvider
{
    /// <summary>
    ///     Provides the current HTTP context.
    /// </summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    ///     Names the claim that carries the tenant identifier.
    /// </summary>
    private readonly ExecutionIdentityOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HttpContextExecutionIdentityProvider" /> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides the current HTTP context.</param>
    /// <param name="options">Names the claim that carries the tenant identifier.</param>
    public HttpContextExecutionIdentityProvider(
        IHttpContextAccessor httpContextAccessor,
        ExecutionIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TenantClaimType);

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

        var tenantId = user.FindFirst(_options.TenantClaimType)?.Value;

        return ExecutionIdentity.For(user, tenantId);
    }
}
