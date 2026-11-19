using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Messaging.Tenancy;

/// <summary>
///     Validates tenant identifiers with the predicate configured on <see cref="TenancyOptions" />.
/// </summary>
/// <remarks>
///     Registered as the <see cref="ITenantIdentifierValidator" /> when <c>UseTenancy</c> builds with a validation
///     predicate. The instance is shared for the life of the process, so the predicate must be thread-safe.
/// </remarks>
internal sealed class PredicateTenantIdentifierValidator : ITenantIdentifierValidator
{
    /// <summary>
    ///     Decides whether a tenant identifier is well formed. Shared for the life of the process.
    /// </summary>
    private readonly Func<string, bool> _isValid;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PredicateTenantIdentifierValidator" /> class.
    /// </summary>
    /// <param name="isValid">Decides whether a tenant identifier is well formed.</param>
    public PredicateTenantIdentifierValidator(Func<string, bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(isValid);

        _isValid = isValid;
    }

    /// <inheritdoc />
    public ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        return new ValueTask<bool>(_isValid(tenantId));
    }
}
