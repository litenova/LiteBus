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
    ///     Verifies invalid UTF-8 bytes are preserved through base64 transport encoding.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_WithInvalidUtf8Body_ShouldBase64EncodeWithoutReplacingBytes()
    {
        var binaryBody = new byte[] { 0xC3, 0x28 };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/binary",
            Body = binaryBody
        });

        sendRequest.MessageBody.Should().Be(Convert.ToBase64String(binaryBody));
        sendRequest.MessageAttributes[TransportHeaders.ContentEncoding].StringValue.Should().Be("base64");

        var transportMessage = SqsMessageMapper.ToTransportMessage(
            new Message
            {
                MessageId = "msg-invalid-utf8",
                Body = sendRequest.MessageBody,
                MessageAttributes = sendRequest.MessageAttributes
            },
            "https://sqs.us-east-1.amazonaws.com/123/binary",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (_, _) => Task.CompletedTask
            });

        transportMessage.Body.ToArray().Should().Equal(binaryBody);
    }

    /// <summary>
    ///     Verifies SQS-disallowed control bytes use the binary body representation.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_WithDisallowedControlByte_ShouldBase64Encode()
    {
        var body = new byte[] { 0x01 };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/control",
            Body = body
        });

        sendRequest.MessageBody.Should().Be(Convert.ToBase64String(body));
        sendRequest.MessageAttributes[TransportHeaders.ContentEncoding].StringValue.Should().Be("base64");
    }

    /// <summary>
    ///     Verifies caller headers cannot override the mapper's binary content marker.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_WithContentEncodingHeaderAndTextBody_ShouldRemoveStaleMarker()
    {
        var sendRequest = SqsMessageMapper.ToSendMessageRequest(new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/text",
            Body = Encoding.UTF8.GetBytes("plain text"),
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContentEncoding] = "base64"
            }
        });

        sendRequest.MessageAttributes.ContainsKey(TransportHeaders.ContentEncoding).Should().BeFalse();
        sendRequest.MessageBody.Should().Be("plain text");
    }

    /// <summary>
    ///     Verifies full durable metadata stays below the SQS ten-attribute limit and expands on receive.
    /// </summary>
    [Fact]
    public void ToSendMessageRequest_WithFullDurableMetadata_ShouldPackHeadersAndRoundTripThem()
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = "message-1",
            [TransportHeaders.ContractName] = "orders.events.shipped",
            [TransportHeaders.ContractVersion] = 2,
            [TransportHeaders.CorrelationId] = "correlation-1",
            [TransportHeaders.CausationId] = "causation-1",
            [TransportHeaders.TenantId] = "tenant-1",
            [TransportHeaders.TraceContext] = "{\"traceparent\":\"00-test\"}",
            [TransportHeaders.IdempotencyKey] = "idempotency-1",
            [TransportHeaders.VisibleAfter] = "2026-06-12T08:00:00.0000000+00:00"
        };

        var sendRequest = SqsMessageMapper.ToSendMessageRequest(new TransportPublishRequest
        {
            Destination = "https://sqs.us-east-1.amazonaws.com/123/orders",
            Route = "orders.events.shipped",
            ContentType = "application/json",
            Body = Encoding.UTF8.GetBytes("{}"),
            MessageId = "message-1",
            CorrelationId = "correlation-1",
            Headers = headers
        });

        sendRequest.MessageAttributes.Count.Should().BeLessThanOrEqualTo(10);
        sendRequest.MessageAttributes.Should().ContainKey("litebus-headers");

        var transportMessage = SqsMessageMapper.ToTransportMessage(
            new Message
            {
                MessageId = "aws-message-id",
                Body = sendRequest.MessageBody,
                MessageAttributes = sendRequest.MessageAttributes
            },
            "https://sqs.us-east-1.amazonaws.com/123/orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (_, _) => Task.CompletedTask
            });

        transportMessage.Headers[TransportHeaders.ContractName].Should().Be("orders.events.shipped");
        transportMessage.Headers[TransportHeaders.ContractVersion].Should().Be("2");
        transportMessage.Headers[TransportHeaders.CausationId].Should().Be("causation-1");
        transportMessage.Headers[TransportHeaders.TenantId].Should().Be("tenant-1");
        transportMessage.Headers[TransportHeaders.TraceContext].Should().Be("{\"traceparent\":\"00-test\"}");
        transportMessage.Route.Should().Be("orders.events.shipped");
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
