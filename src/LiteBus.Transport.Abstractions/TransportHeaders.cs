namespace LiteBus.Transport.Abstractions;

/// <summary>
///     Well-known transport application header names used by LiteBus adapters.
/// </summary>
/// <remarks>
///     Dispatch and ingress packages map inbox and outbox envelope metadata to these headers so messages remain
///     wire-compatible across broker implementations.
/// </remarks>
public static class TransportHeaders
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

    /// <summary>
    ///     Gets the header name for the distributed trace context JSON blob.
    /// </summary>
    public const string TraceContext = "litebus-trace-context";

    /// <summary>
    ///     Gets the header name for an optional idempotency key.
    /// </summary>
    public const string IdempotencyKey = "litebus-idempotency-key";

    /// <summary>
    ///     Gets the header name for an optional visible-after timestamp.
    /// </summary>
    public const string VisibleAfter = "litebus-visible-after";

    /// <summary>
    ///     Gets the header name for an optional relative visibility delay expressed as an ISO 8601 duration or tick count.
    /// </summary>
    public const string VisibleAfterDelay = "litebus-visible-after-delay";

    /// <summary>
    ///     Gets the header name for the SQS message body encoding when the payload is base64-encoded.
    /// </summary>
    public const string ContentEncoding = "litebus-content-encoding";

}
