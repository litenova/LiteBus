namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Resolves transport destinations from tenant metadata.
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
    string ResolveRoute(string? tenantId, string contractName, string? topic);
}
