using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Amqp;

/// <summary>
///     Well-known AMQP application header names used by LiteBus transport adapters.
/// </summary>
/// <remarks>
///     These constants alias <see cref="TransportHeaders" /> so existing AMQP call sites remain wire-compatible
///     during the transport abstraction migration.
/// </remarks>
public static class AmqpHeaders
{
    /// <summary>
    ///     Gets the header name for the stable LiteBus message identifier.
    /// </summary>
    public const string MessageId = TransportHeaders.MessageId;

    /// <summary>
    ///     Gets the header name for the stable message contract name.
    /// </summary>
    public const string ContractName = TransportHeaders.ContractName;

    /// <summary>
    ///     Gets the header name for the message contract version.
    /// </summary>
    public const string ContractVersion = TransportHeaders.ContractVersion;

    /// <summary>
    ///     Gets the header name for the correlation identifier.
    /// </summary>
    public const string CorrelationId = TransportHeaders.CorrelationId;

    /// <summary>
    ///     Gets the header name for the causation identifier.
    /// </summary>
    public const string CausationId = TransportHeaders.CausationId;

    /// <summary>
    ///     Gets the header name for the tenant identifier.
    /// </summary>
    public const string TenantId = TransportHeaders.TenantId;

    /// <summary>
    ///     Gets the header name for the distributed trace context JSON blob.
    /// </summary>
    public const string TraceContext = TransportHeaders.TraceContext;
}
