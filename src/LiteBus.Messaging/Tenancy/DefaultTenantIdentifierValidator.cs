using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Messaging.Tenancy;

/// <summary>
///     Accepts every tenant identifier, so a composition with no configured validation keeps its current behavior.
/// </summary>
/// <remarks>
///     <para>
///         Registered as the <see cref="ITenantIdentifierValidator" /> LiteBus default when <c>UseTenancy</c> builds
///         without a configured validator. The guard and tenant policies check that a tenant is present before they
///         consult the validator, so a blank identifier never reaches this implementation: it answers whether a
///         present tenant is well formed, and with no stricter shape configured every present tenant is.
///     </para>
///     <para>
///         An application with workspace GUIDs or another fixed shape replaces this through
///         <see cref="TenancyOptions" />, and the composition model records the replacement.
///     </para>
/// </remarks>
public sealed class DefaultTenantIdentifierValidator : ITenantIdentifierValidator
{
    /// <inheritdoc />
    public ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(true);
    }
}
