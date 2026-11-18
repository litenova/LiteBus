namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for resolving the LiteBus execution identity from the current ASP.NET Core request.
/// </summary>
public sealed class ExecutionIdentityOptions
{
    /// <summary>
    ///     Gets or sets the claim type that carries the tenant identifier.
    /// </summary>
    /// <value>The claim type. The default is <c>tenant_id</c>.</value>
    public string TenantClaimType { get; set; } = "tenant_id";
}
