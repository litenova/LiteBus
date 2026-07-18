using System.Diagnostics;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies transport activity names, parenting, kinds, and messaging attributes.
/// </summary>
public sealed class TransportTracingTests
{
    /// <summary>
    ///     Verifies send tracing records Kafka producer metadata with current semantic convention names.
    /// </summary>
    [Fact]
    public void StartPublishActivity_ShouldRecordKafkaProducerTags()
    {
        using var listener = CreateListener();

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.Kafka,
            Destination = "orders",
            Route = "customer-42",
            MessageId = "message-1",
            CorrelationId = "correlation-1"
        });

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("send orders");
        activity.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingSystemTagName).Should().Be("kafka");
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingOperationNameTagName).Should().Be("send");
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingOperationTypeTagName).Should().Be("send");
        activity.GetTagItem(LiteBusTransportTelemetry.DestinationTagName).Should().Be("orders");
        activity.GetTagItem(LiteBusTransportTelemetry.RouteTagName).Should().Be("customer-42");
        activity.GetTagItem(LiteBusTransportTelemetry.KafkaMessageKeyTagName).Should().Be("customer-42");
        activity.GetTagItem(LiteBusTransportTelemetry.RabbitMqRoutingKeyTagName).Should().BeNull();
        activity.GetTagItem(LiteBusTransportTelemetry.MessageIdTagName).Should().Be("message-1");
        activity.GetTagItem(LiteBusTransportTelemetry.ConversationIdTagName).Should().Be("correlation-1");
    }

    /// <summary>
    ///     Verifies process tracing records RabbitMQ consumer metadata and redelivery state.
    /// </summary>
    [Fact]
    public void StartConsumeActivity_ShouldRecordRabbitMqConsumerTags()
    {
        using var listener = CreateListener();
        var message = CreateMessage(
            TransportMessagingSystems.RabbitMq,
            new Dictionary<string, object?>(),
            redelivered: true);

        using var activity = TransportTracing.StartConsumeActivity(message);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("process orders");
        activity.Kind.Should().Be(ActivityKind.Consumer);
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingSystemTagName).Should().Be("rabbitmq");
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingOperationNameTagName).Should().Be("process");
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingOperationTypeTagName).Should().Be("process");
        activity.GetTagItem(LiteBusTransportTelemetry.DestinationTagName).Should().Be("orders");
        activity.GetTagItem(LiteBusTransportTelemetry.RouteTagName).Should().Be("customer-42");
        activity.GetTagItem(LiteBusTransportTelemetry.RabbitMqRoutingKeyTagName).Should().Be("customer-42");
        activity.GetTagItem(LiteBusTransportTelemetry.KafkaMessageKeyTagName).Should().BeNull();
        activity.GetTagItem(LiteBusTransportTelemetry.MessageIdTagName).Should().Be("message-1");
        activity.GetTagItem(LiteBusTransportTelemetry.ConversationIdTagName).Should().Be("correlation-1");
        activity.GetTagItem(LiteBusTransportTelemetry.RedeliveredTagName).Should().Be(true);
    }

    /// <summary>
    ///     Verifies blank optional metadata leaves only required messaging attributes.
    /// </summary>
    [Fact]
    public void StartPublishActivity_WithBlankOptionalMetadata_ShouldOmitOptionalTags()
    {
        using var listener = CreateListener();

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.LiteBusInMemory,
            Destination = " ",
            Route = null,
            MessageId = string.Empty,
            CorrelationId = null
        });

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("send");
        activity.TagObjects.Should().HaveCount(3);
        activity.GetTagItem(LiteBusTransportTelemetry.MessagingSystemTagName).Should().Be("litebus_in_memory");
        activity.GetTagItem(LiteBusTransportTelemetry.DestinationTagName).Should().BeNull();
        activity.GetTagItem(LiteBusTransportTelemetry.RouteTagName).Should().BeNull();
        activity.GetTagItem(LiteBusTransportTelemetry.MessageIdTagName).Should().BeNull();
    }

    /// <summary>
    ///     Verifies send spans retain the current application activity as their parent.
    /// </summary>
    [Fact]
    public void StartPublishActivity_WithAmbientActivity_ShouldRetainParent()
    {
        using var listener = CreateListener();
        using var parent = new Activity("application").Start();

        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.AmazonSqs,
            Destination = "orders"
        });

        activity.Should().NotBeNull();
        activity!.TraceId.Should().Be(parent.TraceId);
        activity.ParentSpanId.Should().Be(parent.SpanId);
    }

    /// <summary>
    ///     Verifies process spans continue a serialized W3C trace context when no ambient activity exists.
    /// </summary>
    [Fact]
    public void StartConsumeActivity_WithSerializedTraceContext_ShouldContinueRemoteParent()
    {
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        using var listener = CreateListener();
        var headers = new Dictionary<string, object?>
        {
            [TransportHeaders.TraceContext] = $$"""{"traceparent":"{{traceParent}}","tracestate":"vendor=value"}"""
        };
        var message = CreateMessage(TransportMessagingSystems.ServiceBus, headers, redelivered: false);

        using var activity = TransportTracing.StartConsumeActivity(message);

        activity.Should().NotBeNull();
        activity!.TraceId.ToHexString().Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        activity.ParentSpanId.ToHexString().Should().Be("00f067aa0ba902b7");
        activity.Parent.Should().BeNull();
    }

    /// <summary>
    ///     Verifies failed operations record an error status and stable exception type.
    /// </summary>
    [Fact]
    public void RecordException_ShouldRecordErrorStatusAndType()
    {
        using var listener = CreateListener();
        using var activity = TransportTracing.StartPublishActivity(new TransportActivityMetadata
        {
            MessagingSystem = TransportMessagingSystems.Kafka,
            Destination = "orders"
        });
        var exception = new InvalidOperationException("broker rejected the send");

        TransportTracing.RecordException(activity, exception);

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().BeNull();
        activity.GetTagItem("error.type").Should().Be(typeof(InvalidOperationException).FullName);
    }

    private static TransportMessage CreateMessage(
        string messagingSystem,
        IReadOnlyDictionary<string, object?> headers,
        bool redelivered)
    {
        return new TransportMessage
        {
            MessagingSystem = messagingSystem,
            Body = (byte[])[1, 2, 3],
            Headers = headers,
            Destination = "orders",
            Route = "customer-42",
            MessageId = "message-1",
            CorrelationId = "correlation-1",
            Redelivered = redelivered,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == LiteBusTransportTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
