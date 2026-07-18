namespace LiteBus.DurableMessaging.Abstractions.Processing;

/// <summary>
///     Axis-neutral read-only view of a leased processor envelope used by orchestration hooks.
/// </summary>
/// <remarks>
///     Inbox and outbox processors adapt their durable envelope types to this contract before invoking
///     <see cref="IProcessorEnvelopeHook" /> implementations such as saga state loaders.
/// </remarks>
public interface IProcessorEnvelope
{
    /// <summary>
    ///     Gets the unique persisted message identifier.
    /// </summary>
    Guid MessageId { get; }

    /// <summary>
    ///     Gets the stable message contract name used to resolve handlers and saga state types.
    /// </summary>
    string ContractName { get; }

    /// <summary>
    ///     Gets the message contract version used to resolve the message type and payload shape.
    /// </summary>
    int ContractVersion { get; }

    /// <summary>
    ///     Gets the optional correlation identifier used to correlate orchestration state.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    ///     Gets the optional causation identifier.
    /// </summary>
    string? CausationId { get; }

    /// <summary>
    ///     Gets the optional tenant identifier.
    /// </summary>
    string? TenantId { get; }
}
