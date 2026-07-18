namespace LiteBus.Transport;

/// <summary>
///     Transport-neutral envelope metadata copied onto broker application headers during dispatch.
/// </summary>
/// <param name="MessageId">The stable persisted message identifier.</param>
/// <param name="ContractName">The stable message contract name.</param>
/// <param name="ContractVersion">The message contract version.</param>
/// <param name="CorrelationId">The optional correlation identifier.</param>
/// <param name="CausationId">The optional causation identifier.</param>
/// <param name="TenantId">The optional tenant identifier.</param>
/// <param name="TraceContext">The optional distributed trace context JSON blob.</param>
/// <param name="IdempotencyKey">The optional idempotency key.</param>
/// <param name="VisibleAfter">The optional earliest UTC timestamp at which the message may be processed.</param>
public sealed record TransportEnvelopeHeaderSource(
    Guid MessageId,
    string ContractName,
    int ContractVersion,
    string? CorrelationId,
    string? CausationId,
    string? TenantId,
    string? TraceContext,
    string? IdempotencyKey,
    DateTimeOffset? VisibleAfter);
