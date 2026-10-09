using System.Text;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;

namespace LiteBus.Transport.UnitTests.AzureServiceBus;

/// <summary>
///     Verifies the mapping from a transport publish request to a Service Bus message.
/// </summary>
public sealed class AzureServiceBusMessageMapperTests
{
    [Fact]
    public void ToServiceBusMessage_WithoutMessageId_ShouldMapWithoutThrowing()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Body = Encoding.UTF8.GetBytes("payload")
        };

        var message = AzureServiceBusMessageMapper.ToServiceBusMessage(request);

        message.Body.ToString().Should().Be("payload");
    }

    [Fact]
    public void ToServiceBusMessage_WithMessageId_ShouldCopyIt()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Body = Encoding.UTF8.GetBytes("payload"),
            MessageId = "message-1"
        };

        var message = AzureServiceBusMessageMapper.ToServiceBusMessage(request);

        message.MessageId.Should().Be("message-1");
    }
}
