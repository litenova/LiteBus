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
/// </remarks>
public interface IExecutionIdentityProvider
{
    /// <summary>
    ///     Gets the identity of the current caller.
    /// </summary>
    /// <returns>The current identity, or <see cref="ExecutionIdentity.Anonymous" /> when no caller is known.</returns>
    ExecutionIdentity GetCurrentIdentity();
}
