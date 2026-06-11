namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Resolves transport destinations and processor lease filters from tenant metadata.
/// </summary>
public interface ITenantRoutingStrategy
{
    /// <summary>
    ///     Resolves the transport route for one tenant, contract, and topic combination.
    /// </summary>
    /// <param name="tenantId">The optional tenant identifier from the envelope.</param>
    /// <param name="contractName">The stable contract name.</param>
    /// <param name="topic">The optional topic or destination hint.</param>
    /// <returns>The route passed to the transport publisher.</returns>
    string ResolveDestination(string? tenantId, string contractName, string? topic);

    /// <summary>
    ///     Resolves the tenant identifier processors should use when leasing messages.
    /// </summary>
    /// <param name="tenantId">The tenant identifier configured on the processor.</param>
    /// <returns>
    ///     The tenant filter applied to lease queries, or <see langword="null" /> when the processor should lease all
    ///     tenants.
    /// </returns>
    string? ResolveLeaseFilter(string? tenantId);
}