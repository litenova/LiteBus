using LiteBus.Transport.Abstractions;

namespace LiteBus.DurableTransport.IntegrationTesting;

/// <summary>
///     Builds transport header dictionaries for integration scenarios.
/// </summary>
public static class TransportTestHeaders
{
    /// <summary>
    ///     Creates the standard LiteBus transport headers for a command publish.
    /// </summary>
    /// <param name="messageId">The durable message identifier.</param>
    /// <param name="contractName">The stable contract name.</param>
    /// <param name="contractVersion">The contract version.</param>
    /// <param name="correlationId">The optional correlation identifier.</param>
    /// <param name="causationId">The optional causation identifier.</param>
    /// <param name="tenantId">The optional tenant identifier.</param>
    /// <returns>The header dictionary passed to transport publish requests.</returns>
    public static Dictionary<string, object?> Create(
        Guid messageId,
        string contractName,
        int contractVersion,
        string? correlationId = null,
        string? causationId = null,
        string? tenantId = null)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = messageId.ToString("D"),
            [TransportHeaders.ContractName] = contractName,
            [TransportHeaders.ContractVersion] = contractVersion
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers[TransportHeaders.CorrelationId] = correlationId;
        }

        if (!string.IsNullOrWhiteSpace(causationId))
        {
            headers[TransportHeaders.CausationId] = causationId;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            headers[TransportHeaders.TenantId] = tenantId;
        }

        return headers;
    }
}
