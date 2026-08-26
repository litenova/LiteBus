using System.Globalization;
using System.Text;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Maps between LiteBus transport messages and Amazon SQS broker messages.
/// </summary>
internal static class SqsMessageMapper
{
    /// <summary>
    ///     The base64 content encoding marker stored in SQS message attributes.
    /// </summary>
    private const string Base64ContentEncoding = "base64";

    /// <summary>
    ///     Creates an SQS send request from a LiteBus publish request.
    /// </summary>
    /// <param name="request">The publish request describing destination, body, and headers.</param>
    /// <returns>The send request ready for the SQS client.</returns>
    internal static SendMessageRequest ToSendMessageRequest(TransportPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Body.Span;
        var useBase64 = !IsValidUtf8Text(body);

        // AWSSDK 4 stopped initializing collection properties, so an attribute dictionary assigned here is the only
        // thing that keeps the writes below from dereferencing null.
        var sendRequest = new SendMessageRequest
        {
            QueueUrl = request.Destination,
            MessageBody = useBase64
                ? Convert.ToBase64String(body)
                : Encoding.UTF8.GetString(body),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
        };

        if (useBase64)
        {
            sendRequest.MessageAttributes[TransportHeaders.ContentEncoding] =
                CreateStringAttribute(Base64ContentEncoding);
        }

        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            sendRequest.MessageAttributes[TransportHeaders.MessageId] = CreateStringAttribute(request.MessageId);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            sendRequest.MessageAttributes[TransportHeaders.CorrelationId] =
                CreateStringAttribute(request.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(request.Route))
        {
            sendRequest.MessageAttributes["Route"] = CreateStringAttribute(request.Route);
        }

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            sendRequest.MessageAttributes["ContentType"] = CreateStringAttribute(request.ContentType);
        }

        ApplyHeaders(sendRequest, request.Headers);
        return sendRequest;
    }

    /// <summary>
    ///     Maps a received SQS message to the transport-neutral delivery model.
    /// </summary>
    /// <param name="message">The received SQS message.</param>
    /// <param name="queueUrl">The queue URL configured for the consumer.</param>
    /// <param name="ackHandlers">The acknowledgement handlers wired by the consumer.</param>
    /// <returns>The transport message passed to consumer handlers.</returns>
    internal static TransportMessage ToTransportMessage(
        Message message,
        string queueUrl,
        TransportConsumerAckHandlers ackHandlers)
    {
        ArgumentNullException.ThrowIfNull(ackHandlers);

        var body = DecodeBody(message);
        var headers = CopyMessageAttributes(message.MessageAttributes);

        return new TransportMessage
        {
            MessagingSystem = TransportMessagingSystems.AmazonSqs,
            Body = body,
            Headers = headers,
            Destination = queueUrl,
            Route = GetAttribute(headers, "Route"),
            MessageId = message.MessageId ?? GetAttribute(headers, TransportHeaders.MessageId),
            CorrelationId = GetAttribute(headers, TransportHeaders.CorrelationId),
            Redelivered = message.Attributes?.TryGetValue("ApproximateReceiveCount", out var count) == true &&
                          int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receiveCount) &&
                          receiveCount > 1,
            AckAsync = ackHandlers.AckAsync,
            NackAsync = ackHandlers.NackAsync
        };
    }

    /// <summary>
    ///     Decodes the SQS message body using the optional content-encoding attribute.
    /// </summary>
    /// <param name="message">The received SQS message.</param>
    /// <returns>The decoded message body bytes.</returns>
    private static byte[] DecodeBody(Message message)
    {
        var rawBody = message.Body ?? string.Empty;

        if (message.MessageAttributes?.TryGetValue(TransportHeaders.ContentEncoding, out var encoding) == true &&
            string.Equals(encoding.StringValue, Base64ContentEncoding, StringComparison.Ordinal))
        {
            return Convert.FromBase64String(rawBody);
        }

        return Encoding.UTF8.GetBytes(rawBody);
    }

    /// <summary>
    ///     Returns whether the payload is valid UTF-8 text without embedded null bytes.
    /// </summary>
    /// <param name="body">The publish body bytes.</param>
    /// <returns><see langword="true" /> when the body can be sent as a plain UTF-8 SQS message body.</returns>
    private static bool IsValidUtf8Text(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return true;
        }

        try
        {
            var text = Encoding.UTF8.GetString(body);

            return !text.Contains('\0');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Copies publish headers onto SQS message attributes.
    /// </summary>
    /// <param name="sendRequest">The send request receiving message attributes.</param>
    /// <param name="headers">The optional LiteBus headers.</param>
    private static void ApplyHeaders(SendMessageRequest sendRequest, IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            if (value is null)
            {
                continue;
            }

            sendRequest.MessageAttributes[name] = CreateStringAttribute(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>
    ///     Creates a string-typed SQS message attribute.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The message attribute value.</returns>
    private static MessageAttributeValue CreateStringAttribute(string value)
    {
        return new MessageAttributeValue
        {
            DataType = "String",
            StringValue = value
        };
    }

    /// <summary>
    ///     Copies SQS message attributes into a read-only header dictionary.
    /// </summary>
    /// <param name="attributes">The SQS message attributes, which AWSSDK 4 leaves null when the message carries none.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static Dictionary<string, object?> CopyMessageAttributes(
        Dictionary<string, MessageAttributeValue>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var headers = new Dictionary<string, object?>(attributes.Count, StringComparer.Ordinal);

        foreach (var (name, value) in attributes)
        {
            headers[name] = value.StringValue ?? string.Empty;
        }

        return headers;
    }

    /// <summary>
    ///     Reads a string header value when present.
    /// </summary>
    /// <param name="headers">The header dictionary from the received message.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The header value, or <see langword="null" /> when absent.</returns>
    private static string? GetAttribute(Dictionary<string, object?> headers, string name)
    {
        return headers.TryGetValue(name, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }
}
