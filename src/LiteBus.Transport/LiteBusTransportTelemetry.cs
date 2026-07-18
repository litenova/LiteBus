namespace LiteBus.Transport;

/// <summary>
///     Public OpenTelemetry instrument names for transport telemetry shared across broker adapters.
/// </summary>
public static class LiteBusTransportTelemetry
{
    /// <summary>
    ///     Gets the activity source name used for transport publish and consume spans.
    /// </summary>
    public const string ActivitySourceName = "LiteBus.Transport";

    /// <summary>
    ///     Gets the OpenTelemetry operation name recorded when a transport publisher sends a message.
    /// </summary>
    public const string PublishOperationName = "send";

    /// <summary>
    ///     Gets the OpenTelemetry operation name recorded while a transport consumer processes a message.
    /// </summary>
    public const string ConsumeOperationName = "process";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying the messaging system.
    /// </summary>
    public const string MessagingSystemTagName = "messaging.system";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying the system-specific operation.
    /// </summary>
    public const string MessagingOperationNameTagName = "messaging.operation.name";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying the operation type.
    /// </summary>
    public const string MessagingOperationTypeTagName = "messaging.operation.type";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying the messaging destination.
    /// </summary>
    public const string DestinationTagName = "messaging.destination.name";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying one broker message.
    /// </summary>
    public const string MessageIdTagName = "messaging.message.id";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying a related message conversation.
    /// </summary>
    public const string ConversationIdTagName = "messaging.message.conversation_id";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying a Kafka message key.
    /// </summary>
    public const string KafkaMessageKeyTagName = "messaging.kafka.message.key";

    /// <summary>
    ///     Gets the OpenTelemetry attribute name identifying a RabbitMQ routing key.
    /// </summary>
    public const string RabbitMqRoutingKeyTagName = "messaging.rabbitmq.destination.routing_key";

    /// <summary>
    ///     Gets the LiteBus attribute name identifying a broker-neutral route.
    /// </summary>
    public const string RouteTagName = "litebus.transport.route";

    /// <summary>
    ///     Gets the LiteBus attribute name indicating whether a broker redelivered a message.
    /// </summary>
    public const string RedeliveredTagName = "litebus.transport.redelivered";

    /// <summary>
    ///     Gets the meter name used for transport circuit breaker metrics.
    /// </summary>
    public const string MeterName = "LiteBus.Transport";

    /// <summary>
    ///     Gets the instrument name indicating whether the transport circuit breaker is open.
    /// </summary>
    public const string CircuitBreakerOpenInstrumentName = "litebus.transport.circuit_breaker.open";

    /// <summary>
    ///     Gets the instrument name for the current transport circuit breaker failure count.
    /// </summary>
    public const string CircuitBreakerFailureCountInstrumentName = "litebus.transport.circuit_breaker.failure_count";

    /// <summary>
    ///     Gets the OpenTelemetry tag name identifying the transport broker adapter on circuit breaker metrics.
    /// </summary>
    public const string BrokerTagName = "litebus.transport.broker";
}
