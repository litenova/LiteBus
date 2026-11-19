using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Decides whether a tenant identifier is well formed.
/// </summary>
/// <remarks>
///     <para>
///         This is the port tenant format validation reads through. The tenant policy, the tenancy guard and tenant
///         sources consult it, so one implementation covers every path that accepts a tenant. A malformed tenant is
///         refused with <see cref="TenancyRefusalCodes.Invalid" />, distinct from
///         <see cref="TenancyRefusalCodes.TenantRequired" />.
///     </para>
///     <para>
///         With no validator registered, a tenant is accepted when it is not blank. Register one implementation when
///         tenant identifiers follow a stricter shape, such as workspace GUIDs.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class WorkspaceTenantValidator : ITenantIdentifierValidator
/// {
///     public ValueTask<bool> IsValidAsync(
///         string tenantId,
///         IServiceProvider services,
///         CancellationToken cancellationToken)
///     {
///         return new ValueTask<bool>(Guid.TryParse(tenantId, out _));
///     }
/// }
/// ]]></code>
/// </example>
public interface ITenantIdentifierValidator
{
    /// <summary>
    ///     Determines whether a tenant identifier is well formed.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to check.</param>
    /// <param name="services">The service provider the check resolves its dependencies from.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns><see langword="true" /> when the identifier is well formed.</returns>
    ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken);
}
