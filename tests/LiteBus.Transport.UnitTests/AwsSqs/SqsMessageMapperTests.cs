using System.Text;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AwsSqs;

namespace LiteBus.Transport.UnitTests.AwsSqs;

/// <summary>
///     Verifies SQS message mapping for publish and consume paths.
/// </summary>
public sealed class SqsMessageMapperTests
{
    /// <summary>
    ///     Verifies publish requests map queue URL, body, and LiteBus headers to SQS attributes.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_ShouldMapBodyAndHeaders()
    {
        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders",
            Route = "ship",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"7"}"""),
            MessageId = Guid.NewGuid().ToString("D"),
            CorrelationId = "corr-aws",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = 2
            }
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.QueueUrl.Should().Be(request.Destination);
        sendRequest.MessageBody.Should().Contain("orderId");

        sendRequest.MessageAttributes[TransportHeaders.ContractName].StringValue
            .Should().Be("orders.commands.ship");

        sendRequest.MessageAttributes[TransportHeaders.ContractVersion].StringValue.Should().Be("2");
        sendRequest.MessageAttributes["Route"].StringValue.Should().Be("ship");
        sendRequest.MessageAttributes[TransportHeaders.CorrelationId].StringValue.Should().Be("corr-aws");
        sendRequest.MessageAttributes.Should().NotContainKey(TransportHeaders.LegacyCorrelationId);
    }

    /// <summary>
    ///     Verifies non-UTF-8 bodies are base64-encoded with a content-encoding attribute.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_WithBinaryBody_ShouldBase64Encode()
    {
        var binaryBody = new byte[] { 0x00, 0x01, 0xFF };

        var request = new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/binary",
            Body = binaryBody
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(request);

        sendRequest.MessageBody.Should().Be(Convert.ToBase64String(binaryBody));
        sendRequest.MessageAttributes[TransportHeaders.ContentEncoding].StringValue.Should().Be("base64");
    }

    /// <summary>
    ///     Verifies base64-encoded bodies round-trip through consume mapping.
    /// </summary>
    [Fact]
    public void ToTransportMessage_WithBase64Body_ShouldDecodeBytes()
    {
        var binaryBody = new byte[] { 0x00, 0x01, 0xFF };

        var message = new Message
        {
            MessageId = "msg-binary",
            Body = Convert.ToBase64String(binaryBody),
            ReceiptHandle = "receipt-binary",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContentEncoding] = new()
                {
                    DataType = "String",
                    StringValue = "base64"
                }
            }
        };

        var transportMessage = SqsMessageMapper.ToTransportMessage(
            message,
            "https://sqs.us-east-1.amazonaws.com/123/binary",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (_, _) => Task.CompletedTask
            });

        transportMessage.Body.ToArray().Should().Equal(binaryBody);
    }

    /// <summary>
    ///     Verifies received SQS messages map to transport messages with acknowledgement delegates.
    /// </summary>
    [Fact]
    public async Task ToTransportMessage_ShouldExposeAckDelegates()
    {
        var deleted = false;

        var message = new Message
        {
            MessageId = "msg-1",
            Body = """{"ok":true}""",
            ReceiptHandle = "receipt-1",
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = new()
                {
                    DataType = "String",
                    StringValue = "orders.commands.ship"
                }
            },
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ApproximateReceiveCount"] = "2"
            }
        };

        var transportMessage = SqsMessageMapper.ToTransportMessage(
            message,
            "https://sqs.us-east-1.amazonaws.com/123/orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ =>
                {
                    deleted = true;
                    return Task.CompletedTask;
                },
                NackAsync = (_, _) => Task.CompletedTask
            });

        transportMessage.Redelivered.Should().BeTrue();
        transportMessage.Headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");

        await transportMessage.AcceptAsync().ConfigureAwait(false);
        deleted.Should().BeTrue();
    }
}
