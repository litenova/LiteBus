using System.Text;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies SQS FIFO queue mapping and canonical route and content type header names.
/// </summary>
public sealed class SqsFifoMessageMapperTests
{
    /// <summary>
    ///     Verifies a FIFO queue URL carries the deduplication and group identifiers.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_FifoQueueUrl_SetsDeduplicationAndGroupIds()
    {
        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders.fifo",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"7"}"""),
            MessageGroupId = "tenant-a",
            DeduplicationKey = "dedup-1"
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.MessageDeduplicationId.Should().Be("dedup-1");
        sendRequest.MessageGroupId.Should().Be("tenant-a");
    }

    /// <summary>
    ///     Verifies a standard queue URL carries neither FIFO identifier.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_StandardQueueUrl_OmitsDeduplicationAndGroupIds()
    {
        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"7"}"""),
            MessageGroupId = "tenant-a",
            DeduplicationKey = "dedup-1"
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.MessageDeduplicationId.Should().BeNull();
        sendRequest.MessageGroupId.Should().BeNull();
    }

    /// <summary>
    ///     Verifies a FIFO queue URL without FIFO fields leaves the identifiers unset.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_FifoQueueUrlWithoutFifoFields_LeavesIdsUnset()
    {
        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders.fifo",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"7"}""")
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.MessageDeduplicationId.Should().BeNull();
        sendRequest.MessageGroupId.Should().BeNull();
    }

    /// <summary>
    ///     Verifies received messages read the route under the canonical header name.
    /// </summary>
    [Fact]
    public void ToTransportMessage_CanonicalRouteAttribute_MapsToRoute()
    {
        var message = new Message
        {
            MessageId = "msg-route",
            Body = """{"ok":true}""",
            ReceiptHandle = "receipt-route",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                [TransportHeaders.Route] = new()
                {
                    DataType = "String",
                    StringValue = "ship"
                }
            }
        };

        var transportMessage = SqsMessageMapper.ToTransportMessage(
            message,
            "https://sqs.us-east-1.amazonaws.com/123/orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (_, _) => Task.CompletedTask
            });

        transportMessage.Route.Should().Be("ship");
    }
}
