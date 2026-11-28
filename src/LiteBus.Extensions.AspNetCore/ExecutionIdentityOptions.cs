using System;
using System.Collections.Generic;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for resolving the LiteBus execution identity from the current ASP.NET Core request.
/// </summary>
public sealed class ExecutionIdentityOptions
{
    /// <summary>
    ///     Gets or sets the claim types that carry the tenant identifier, in the order they are tried.
    /// </summary>
    /// <value>
    ///     The ordered claim types. The default is a single <c>tenant_id</c> entry. The provider reads the first
    ///     entry carrying a non-blank value.
    /// </value>
    public IReadOnlyList<string> TenantClaimTypes { get; set; } = ["tenant_id"];

    /// <summary>
    ///     Validates that at least one tenant claim type is configured and that every entry names a claim.
    /// </summary>
    /// <exception cref="ArgumentNullException"><see cref="TenantClaimTypes" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <see cref="TenantClaimTypes" /> is empty or names a blank claim type.
    /// </exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(TenantClaimTypes);

        if (TenantClaimTypes.Count == 0)
        {
            throw new ArgumentException(
                "At least one tenant claim type is required, so the provider knows which claim carries the tenant identifier.",
                nameof(TenantClaimTypes));
        }

        foreach (var claimType in TenantClaimTypes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(claimType);
        }
    }
}
