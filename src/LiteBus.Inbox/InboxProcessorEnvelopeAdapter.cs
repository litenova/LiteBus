using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;

namespace LiteBus.Inbox;

/// <summary>
///     Adapts a leased <see cref="InboxEnvelope" /> to the orchestration processor envelope contract.
/// </summary>
internal sealed class InboxProcessorEnvelopeAdapter : IProcessorEnvelope
{
    /// <summary>
    ///     The inbox envelope being adapted.
    /// </summary>
    private readonly InboxEnvelope _envelope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessorEnvelopeAdapter" /> class.
    /// </summary>
    /// <param name="envelope">The inbox envelope to expose through the orchestration contract.</param>
    public InboxProcessorEnvelopeAdapter(InboxEnvelope envelope)
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