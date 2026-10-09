using System.Text;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies transport publish request defaults and FIFO fields.
/// </summary>
public sealed class TransportPublishRequestTests
{
    /// <summary>
    ///     Verifies a new request requires broker routing by default and leaves FIFO fields unset.
    /// </summary>
    [Fact]
    public void NewRequest_ShouldDefaultToMandatoryWithoutFifoFields()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"9"}""")
        };

        request.Mandatory.Should().BeTrue();
        request.Persistent.Should().BeTrue();
        request.MessageGroupId.Should().BeNull();
        request.DeduplicationKey.Should().BeNull();
    }

    /// <summary>
    ///     Verifies FIFO group and deduplication values round-trip through the request.
    /// </summary>
    [Fact]
    public void NewRequest_WithFifoFields_ShouldRetainValues()
    {
        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders.fifo",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"9"}"""),
            MessageGroupId = "tenant-a",
            DeduplicationKey = "order-9"
        };

        request.MessageGroupId.Should().Be("tenant-a");
        request.DeduplicationKey.Should().Be("order-9");
    }
}
