using System.Text;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies Kafka messages carry the shared wire header names.
/// </summary>
public sealed class KafkaWireHeaderTests
{
    /// <summary>
    ///     Verifies the shared route and content type header names use the canonical wire values.
    /// </summary>
    [Fact]
    public void SharedHeaders_ShouldUseCanonicalWireNames()
    {
        TransportHeaders.Route.Should().Be("litebus-route");
        TransportHeaders.ContentType.Should().Be("content-type");
    }

    /// <summary>
    ///     Verifies publish requests write the content type under the shared header name.
    /// </summary>
    [Fact]
    public void ToKafkaMessage_ShouldWriteContentTypeUnderSharedHeaderName()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"3"}"""),
            ContentType = "application/json"
        };

        var kafkaMessage = KafkaMessageMapper.ToKafkaMessage(request);

        KafkaMessageMapperTestsHelpers.GetHeader(kafkaMessage.Headers, TransportHeaders.ContentType)
            .Should().Be("application/json");
    }
}
