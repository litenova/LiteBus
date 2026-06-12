using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Configures the AMQP inbox ingress consumer and broker connection settings.
/// </summary>
public sealed record AmqpInboxIngressOptions
{
    /// <summary>
    ///     Gets the broker connection settings used by the ingress consumer.
    /// </summary>
    public AmqpConnectionOptions Connection { get; init; } = new();

    /// <summary>
    ///     Gets the queue name the ingress consumer subscribes to.
    /// </summary>
    public string QueueName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the maximum number of unacknowledged deliveries prefetched by the broker.
    /// </summary>
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    ///     Gets a value indicating whether the consumer should declare the queue before subscribing.
    /// </summary>
    public bool DeclareQueue { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether a declared queue should survive broker restarts.
    /// </summary>
    public bool DurableQueue { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether failed store writes should be requeued by the broker.
    /// </summary>
    public bool RequeueOnFailure { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether LiteBus application headers may override broker-scoped idempotency and tenant.
    /// </summary>
    public bool TrustApplicationHeaders { get; init; }

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