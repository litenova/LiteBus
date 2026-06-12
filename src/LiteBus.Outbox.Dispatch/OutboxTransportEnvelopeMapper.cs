using System;
using System.Collections.Generic;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport;

namespace LiteBus.Outbox.Dispatch;

/// <summary>
///     Maps outbox envelope metadata to transport publish headers.
/// </summary>
internal static class OutboxTransportEnvelopeMapper
{
    /// <summary>
    ///     Builds LiteBus application headers from an outbox envelope.
    /// </summary>
    /// <param name="envelope">The outbox envelope whose metadata should be copied to transport headers.</param>
    /// <returns>The header dictionary passed to the transport publisher.</returns>
    public static Dictionary<string, object?> BuildHeaders(OutboxEnvelope envelope)
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
