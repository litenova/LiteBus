using System.Globalization;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport;

/// <summary>
///     Maps durable envelope metadata to canonical LiteBus transport application headers.
/// </summary>
public static class TransportEnvelopeHeaderMapper
{
    /// <summary>
    ///     Builds LiteBus application headers from transport-neutral envelope metadata.
    /// </summary>
    /// <param name="source">The envelope metadata copied to transport headers.</param>
    /// <returns>The header dictionary passed to the transport publisher.</returns>
    public static Dictionary<string, object?> BuildHeaders(TransportEnvelopeHeaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = source.MessageId.ToString("D"),
            [TransportHeaders.ContractName] = source.ContractName,
            [TransportHeaders.ContractVersion] = source.ContractVersion
        };

        AddOptionalHeader(headers, TransportHeaders.CorrelationId, source.CorrelationId);
        AddOptionalHeader(headers, TransportHeaders.CausationId, source.CausationId);
        AddOptionalHeader(headers, TransportHeaders.TenantId, source.TenantId);
        AddOptionalHeader(headers, TransportHeaders.TraceContext, source.TraceContext);
        AddOptionalHeader(headers, TransportHeaders.IdempotencyKey, source.IdempotencyKey);

        if (source.VisibleAfter is not null)
        {
            headers[TransportHeaders.VisibleAfter] = source.VisibleAfter.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        return headers;
    }

    /// <summary>
    ///     Adds one optional header when the value is present.
    /// </summary>
    /// <param name="headers">The header dictionary being built.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The optional header value.</param>
    private static void AddOptionalHeader(Dictionary<string, object?> headers, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            headers[name] = value;
        }
    }
}
