using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     Decides whether the permission check behind <see cref="MessagePolicy.Permission" /> passes for an identity.
/// </summary>
/// <remarks>
///     <para>
///         This is the port a permission policy reads through. LiteBus carries the permission name from the policy to
///         this lookup and never interprets it: whether <c>vaults.open</c> is a role, a scope, or a row in a permissions
///         table stays in the application. Register one implementation when a composition uses
///         <see cref="MessagePolicy.Permission" />; a permission policy evaluated with none registered denies with
///         <c>authorization.permission-lookup-missing</c> rather than guessing.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class RolePermissionLookup : IPermissionLookup
/// {
///     public ValueTask<bool> HasPermissionAsync(
///         ExecutionIdentity identity,
///         string permission,
///         AuthorizationContext context,
///         CancellationToken cancellationToken)
///     {
///         return new ValueTask<bool>(identity.Principal?.IsInRole(permission) == true);
///     }
/// }
/// ]]></code>
/// </example>
public interface IPermissionLookup
{
    /// <summary>
    ///     Determines whether an identity holds a permission for the message being mediated.
    /// </summary>
    /// <param name="identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
    /// <param name="permission">The permission the policy requires, in the application's own vocabulary.</param>
    /// <param name="context">The message, identity, services, and handled instant the evaluation reads.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns><see langword="true" /> when the identity holds the permission.</returns>
    ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken);
}
