using System;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Adapts a leased <see cref="OutboxEnvelope" /> to the orchestration processor envelope contract.
/// </summary>
internal sealed class OutboxProcessorEnvelopeAdapter : IProcessorEnvelope
{
    /// <summary>
    ///     The outbox envelope being adapted.
    /// </summary>
    private readonly OutboxEnvelope _envelope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorEnvelopeAdapter" /> class.
    /// </summary>
    /// <param name="envelope">The outbox envelope to expose through the orchestration contract.</param>
    public OutboxProcessorEnvelopeAdapter(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _envelope = envelope;
    }

    /// <inheritdoc />
    public Guid MessageId => _envelope.Id;

    /// <inheritdoc />
    public string ContractName => _envelope.ContractName;

    /// <inheritdoc />
    public int ContractVersion => _envelope.ContractVersion;

    /// <inheritdoc />
    public string? CorrelationId => _envelope.CorrelationId;

    /// <inheritdoc />
    public string? CausationId => _envelope.CausationId;

    /// <inheritdoc />
    public string? TenantId => _envelope.TenantId;
}