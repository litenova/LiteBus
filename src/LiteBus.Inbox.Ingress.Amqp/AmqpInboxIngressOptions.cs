using LiteBus.Amqp;

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
}
