using System;
using System.Collections.Generic;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Dispatch;

/// <summary>
///     Maps inbox envelope metadata to transport publish headers.
/// </summary>
internal static class InboxTransportEnvelopeMapper
{
    /// <summary>
    ///     Builds LiteBus application headers from an inbox envelope.
    /// </summary>
    /// <param name="envelope">The inbox envelope whose metadata should be copied to transport headers.</param>
    /// <returns>The header dictionary passed to the transport publisher.</returns>
    public static Dictionary<string, object?> BuildHeaders(InboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = envelope.Id.ToString("D"),
            [TransportHeaders.ContractName] = envelope.ContractName,
            [TransportHeaders.ContractVersion] = envelope.ContractVersion
        };

        AddOptionalHeader(headers, TransportHeaders.CorrelationId, envelope.CorrelationId);
        AddOptionalHeader(headers, TransportHeaders.CausationId, envelope.CausationId);
        AddOptionalHeader(headers, TransportHeaders.TenantId, envelope.TenantId);
        AddOptionalHeader(headers, TransportHeaders.TraceContext, envelope.TraceContext);

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
