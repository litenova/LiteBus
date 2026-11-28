namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Supplies the ambient <see cref="ExecutionIdentity" /> for a mediation that was not given one explicitly.
/// </summary>
/// <remarks>
///     <para>
///         The host registers one implementation, usually scoped, that reads the caller from where the host keeps it:
///         <c>HttpContext.User</c> and a tenant claim in ASP.NET Core (<c>LiteBus.Extensions.AspNetCore</c> ships one),
///         or a job's own context in a worker. The mediator resolves it from the dispatch scope only when neither the
///         mediation settings nor an enclosing mediation supplied an identity.
///     </para>
///     <para>
///         Return <see cref="ExecutionIdentity.Anonymous" /> rather than <see langword="null" /> when no caller is
///         known.
///     </para>
///     <para>
///         The mediator resolves the provider from the dispatch scope, which is a sibling of the request scope rather
///         than a child of it. A service the request scope holds, such as a principal holder the application fills
///         during authentication, resolves as a fresh instance with no state inside the provider. An implementation
///         that reads such a holder compiles and always observes the default, so every request silently resolves as
///         <see cref="ExecutionIdentity.Anonymous" />.
///     </para>
///     <para>
///         In ASP.NET Core, bridge through <c>IHttpContextAccessor</c> instead: read the principal from
///         <c>HttpContext.User</c> and the tenant from a claim, both of which stay available outside the request
///         scope. An application that keeps the caller in its own request-scoped holder stamps the same values onto
///         claims or the HTTP context so the provider reads them there.
///     </para>
/// </remarks>
public interface IExecutionIdentityProvider
{
    /// <summary>
    ///     Gets the identity of the current caller.
    /// </summary>
    /// <returns>The current identity, or <see cref="ExecutionIdentity.Anonymous" /> when no caller is known.</returns>
    ExecutionIdentity GetCurrentIdentity();
}
