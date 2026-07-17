namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Configures the AMQP inbox ingress consumer.
/// </summary>
/// <remarks>
///     <para>
///         AMQP ingress maps RabbitMQ message identifiers to broker-scoped inbox identity and idempotency through
///         <see cref="TransportInboxIngressSafetyOptions.RequireStableIdentity" /> and
///         <see cref="TransportInboxIngressSafetyOptions.TrustApplicationHeaders" /> on <see cref="Safety" />. When
///         <see cref="TransportInboxIngressSafetyOptions.TrustApplicationHeaders" /> is <see langword="false" /> (the
///         default), the broker delivery id
///         drives deduplication even when publishers attach LiteBus application headers.
///     </para>
///     <para>
///         When store accept succeeds but the AMQP acknowledgement fails, the ingress consumer requeues the delivery so
///         RabbitMQ redelivery is idempotently absorbed by the existing inbox row.
///     </para>
/// </remarks>
public sealed record AmqpInboxIngressOptions
{
    /// <summary>
    ///     Gets the provider-neutral ingress safety settings.
    /// </summary>
    public TransportInboxIngressSafetyOptions Safety { get; init; } = new();

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

}
