namespace LiteBus.Amqp;

/// <summary>
///     Well-known AMQP application header names used by LiteBus transport adapters.
/// </summary>
/// <remarks>
///     Dispatch and ingress packages map inbox and outbox envelope metadata to these headers so messages remain
///     wire-compatible across RabbitMQ and LavinMQ brokers.
/// </remarks>
public static class AmqpHeaders
{
    /// <summary>
    ///     Gets the header name for the stable LiteBus message identifier.
    /// </summary>
    public const string MessageId = "litebus-message-id";

    /// <summary>
    ///     Gets the header name for the stable message contract name.
    /// </summary>
    public const string ContractName = "litebus-contract-name";

    /// <summary>
    ///     Gets the header name for the message contract version.
    /// </summary>
    public const string ContractVersion = "litebus-contract-version";

    /// <summary>
    ///     Gets the header name for the correlation identifier.
    /// </summary>
    public const string CorrelationId = "correlation-id";

    /// <summary>
    ///     Gets the header name for the causation identifier.
    /// </summary>
    public const string CausationId = "causation-id";

    /// <summary>
    ///     Gets the header name for the tenant identifier.
    /// </summary>
    public const string TenantId = "tenant-id";
}
