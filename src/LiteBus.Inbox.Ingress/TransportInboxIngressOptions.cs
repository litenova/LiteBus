using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Configures the transport-neutral inbox ingress consumer loop.
/// </summary>
/// <remarks>
///     <para>
///         Ingress defaults to broker-scoped idempotency derived from the transport delivery id so broker redelivery
///         after a successful store accept does not create duplicate inbox rows. Set
///         <see cref="TrustApplicationHeaders" /> to <see langword="true" /> only when the broker binding is authenticated
///         and upstream publishers are trusted to supply <c>litebus-idempotency-key</c> and <c>litebus-tenant-id</c>.
///     </para>
///     <para>
///         Use <see cref="AuthorizeDeliveryAsync" /> to reject deliveries before deserialization when the host enforces
///         tenant, contract, or size policy at the edge.
///     </para>
/// </remarks>
public sealed record TransportInboxIngressOptions
{
    /// <summary>
    ///     Gets the default maximum ingress body size in bytes (4 MiB).
    /// </summary>
    public const int DefaultMaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>
    ///     Gets the destination address the ingress consumer subscribes to.
    /// </summary>
    public string Destination { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by the broker.
    /// </summary>
    public ushort PrefetchCount { get; init; }

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
    ///     Gets a value indicating whether the consumer should buffer deliveries and call batch inbox accept.
    /// </summary>
    /// <value>
    ///     Default is <see langword="false" />. When <see langword="true" />, the consumer flushes buffered deliveries
    ///     after reaching <see cref="PrefetchCount" />, when <see cref="BatchMaxWait" /> elapses, or when the ingress loop
    ///     stops.
    /// </value>
    public bool EnableBatchAccept { get; init; }

    /// <summary>
    ///     Gets the maximum time buffered deliveries may wait before a partial batch is flushed.
    /// </summary>
    /// <value>
    ///     Default is 200 milliseconds. Applies only when <see cref="EnableBatchAccept" /> is <see langword="true" />.
    ///     Low-traffic queues still accept within this delay even when fewer than <see cref="PrefetchCount" /> messages
    ///     arrive.
    /// </value>
    public TimeSpan BatchMaxWait { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Gets the maximum delivery body size accepted by ingress. Zero disables the limit.
    /// </summary>
    /// <value>Default is <see cref="DefaultMaxMessageBytes" /> (4 MiB).</value>
    public int MaxMessageBytes { get; init; } = DefaultMaxMessageBytes;

    /// <summary>
    ///     Gets a value indicating whether ingress requires a stable broker delivery id for identity and idempotency.
    /// </summary>
    /// <value>Default is <see langword="true" />. When <see langword="false" />, missing broker ids fall back to generated identity.</value>
    public bool RequireStableIdentity { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether LiteBus application headers may override broker-scoped idempotency and tenant.
    /// </summary>
    /// <value>
    ///     Default is <see langword="false" />. Enable only on authenticated broker bindings where upstream publishers
    ///     are trusted.
    /// </value>
    public bool TrustApplicationHeaders { get; init; }

    /// <summary>
    ///     Gets an optional callback invoked before inbox accept to authorize or reject a delivery.
    /// </summary>
    /// <value>
    ///     When supplied, a thrown exception follows the same requeue and discard policy as store accept failures.
    /// </value>
    public Func<TransportMessage, CancellationToken, Task>? AuthorizeDeliveryAsync { get; init; }
}