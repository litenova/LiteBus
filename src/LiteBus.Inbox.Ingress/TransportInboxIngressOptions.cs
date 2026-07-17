using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Configures the transport-neutral inbox ingress consumer loop.
/// </summary>
/// <remarks>
///     <para>
///         Ingress defaults to broker-scoped identity and idempotency derived from the transport delivery id
///         (<c>litebus-message-id</c> header or <see cref="TransportMessage.MessageId" />). When
///         <see cref="TransportInboxIngressSafetyOptions.RequireStableIdentity" /> is <see langword="true" /> (the
///         default), missing broker delivery ids fail
///         closed so redelivery after a successful store accept can resolve the existing inbox row instead of creating a
///         duplicate. Set <see cref="TransportInboxIngressSafetyOptions.RequireStableIdentity" /> to
///         <see langword="false" /> only when generated identity
///         is acceptable and duplicate rows on broker redelivery can be tolerated.
///     </para>
///     <para>
///         Set <see cref="TransportInboxIngressSafetyOptions.TrustApplicationHeaders" /> to <see langword="true" />
///         only when the broker binding is
///         authenticated and upstream publishers are trusted. When trusted, <c>litebus-message-id</c> may supply identity,
///         <c>litebus-idempotency-key</c> overrides broker-scoped deduplication, and <c>tenant-id</c> scopes the
///         accepted envelope. When <see langword="false" /> (the default), identity and idempotency always derive from the
///         broker delivery id regardless of application headers on the wire.
///     </para>
///     <para>
///         When store accept succeeds but broker acknowledgement fails, the consumer negative-acknowledges with requeue so
///         the broker redelivers the message. Stable broker-scoped idempotency absorbs the redelivery into the existing
///         inbox row created on the first attempt.
///     </para>
///     <para>
///         Use <see cref="TransportInboxIngressSafetyOptions.AuthorizeDeliveryAsync" /> to reject deliveries before
///         deserialization when the host enforces
///         tenant, contract, or size policy at the edge.
///     </para>
/// </remarks>
public sealed record TransportInboxIngressOptions
{
    /// <summary>
    ///     Gets the default maximum ingress body size in bytes (4 MiB).
    /// </summary>
    public const int DefaultMaxMessageBytes = TransportInboxIngressSafetyOptions.DefaultMaxMessageBytes;

    /// <summary>
    ///     Gets the destination address the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the optional subscription name for topic-based broker destinations.
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by brokers that support native prefetch.
    /// </summary>
    public int PrefetchCount { get; init; }

    /// <summary>
    ///     Gets the number of messages requested per receive call by transports that receive a broker batch.
    /// </summary>
    public int ReceiveBatchSize { get; init; } = 1;

    /// <summary>
    ///     Gets the optional native callback concurrency for transports that expose a separate handler limit.
    /// </summary>
    public int? MaxConcurrentCalls { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the consumer should declare the destination before subscribing.
    /// </summary>
    public bool DeclareDestination { get; init; }

    /// <summary>
    ///     Gets a value indicating whether a declared destination should survive broker restarts.
    /// </summary>
    public bool DurableDestination { get; init; }

    /// <summary>
    ///     Gets a value indicating whether failed store writes should be requeued by the broker.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;

    /// <summary>
    ///     Gets the provider-neutral ingress safety settings used by broker adapters.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();
}
