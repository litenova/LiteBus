using System;
using System.Collections.Generic;
using LiteBus.Inbox.Abstractions;
using LiteBus.Transport;

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

        return TransportEnvelopeHeaderMapper.BuildHeaders(new TransportEnvelopeHeaderSource(
            envelope.Id,
            envelope.ContractName,
            envelope.ContractVersion,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.TenantId,
            envelope.TraceContext,
            envelope.IdempotencyKey,
            envelope.VisibleAfter));
    }
}
