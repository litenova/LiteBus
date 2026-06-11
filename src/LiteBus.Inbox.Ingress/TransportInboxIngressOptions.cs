using System;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Configures the transport-neutral inbox ingress consumer loop.
/// </summary>
public sealed record TransportInboxIngressOptions
{
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
}