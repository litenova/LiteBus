namespace LiteBus.Transport;

/// <summary>
///     Defines OpenTelemetry messaging system identifiers emitted by LiteBus transport adapters.
/// </summary>
public static class TransportMessagingSystems
{
    /// <summary>
    ///     Gets the messaging system identifier used by Amazon SQS.
    /// </summary>
    public const string AmazonSqs = "aws_sqs";

    /// <summary>
    ///     Gets the messaging system identifier used by Apache Kafka.
    /// </summary>
    public const string Kafka = "kafka";

    /// <summary>
    ///     Gets the messaging system identifier used by the LiteBus in-memory transport.
    /// </summary>
    public const string LiteBusInMemory = "litebus_in_memory";

    /// <summary>
    ///     Gets the messaging system identifier used when a custom transport does not provide a more specific value.
    /// </summary>
    public const string Other = "litebus";

    /// <summary>
    ///     Gets the messaging system identifier used by RabbitMQ-compatible AMQP adapters.
    /// </summary>
    public const string RabbitMq = "rabbitmq";

    /// <summary>
    ///     Gets the messaging system identifier used by Azure Service Bus.
    /// </summary>
    public const string ServiceBus = "servicebus";
}
