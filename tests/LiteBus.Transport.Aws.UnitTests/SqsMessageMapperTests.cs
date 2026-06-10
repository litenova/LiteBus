using System.Text;
using Amazon.SQS.Model;
using AwesomeAssertions;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Aws;

namespace LiteBus.Transport.Aws.UnitTests;

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
                [TransportHeaders.ContractName] = new MessageAttributeValue
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
            new AwsSqsTransportOptions(),
            _ =>
            {
                deleted = true;
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);

        transportMessage.Redelivered.Should().BeTrue();
        transportMessage.Headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");

        await transportMessage.AcceptAsync();
        deleted.Should().BeTrue();
    }
}

