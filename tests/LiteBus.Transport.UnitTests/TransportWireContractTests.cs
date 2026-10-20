using System.Text;
using AwesomeAssertions;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Documents the canonical LiteBus wire header contract shared across broker adapters.
/// </summary>
public sealed class TransportWireContractTests
{
    /// <summary>
    ///     Verifies canonical transport header names used by dispatch and ingress mappers.
    /// </summary>
    [Fact]
    public void CanonicalHeaders_ShouldUseStableWireNames()
    {
        TransportHeaders.MessageId.Should().Be("litebus-message-id");
        TransportHeaders.ContractName.Should().Be("litebus-contract-name");
        TransportHeaders.ContractVersion.Should().Be("litebus-contract-version");
        TransportHeaders.CorrelationId.Should().Be("correlation-id");
        TransportHeaders.CausationId.Should().Be("causation-id");
        TransportHeaders.TenantId.Should().Be("tenant-id");
        TransportHeaders.TraceContext.Should().Be("litebus-trace-context");
        TransportHeaders.IdempotencyKey.Should().Be("litebus-idempotency-key");
        TransportHeaders.VisibleAfter.Should().Be("litebus-visible-after");
        TransportHeaders.ContentEncoding.Should().Be("litebus-content-encoding");
        TransportHeaders.Route.Should().Be("litebus-route");
        TransportHeaders.ContentType.Should().Be("content-type");
    }

    /// <summary>
    ///     Verifies the SQS and Kafka adapters publish route and content type under the canonical names.
    /// </summary>
    [Fact]
    public void AdapterMappers_ShouldUseCanonicalRouteAndContentTypeNames()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Route = "tenant-a",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"9"}"""),
            ContentType = "application/json"
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.MessageAttributes.Should().ContainKey(TransportHeaders.Route);
        sendRequest.MessageAttributes[TransportHeaders.Route].StringValue.Should().Be("tenant-a");
        sendRequest.MessageAttributes.Should().ContainKey(TransportHeaders.ContentType);
        sendRequest.MessageAttributes.Should().NotContainKey("Route");
        sendRequest.MessageAttributes.Should().NotContainKey("ContentType");

        var kafkaMessage = KafkaMessageMapper.ToKafkaMessage(request);
        var headerNames = kafkaMessage.Headers.Select(header => header.Key).ToList();

        headerNames.Should().Contain(TransportHeaders.ContentType);
        headerNames.Should().NotContain("ContentType");
    }
}
