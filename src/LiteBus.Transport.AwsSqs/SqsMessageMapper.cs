using System.Globalization;
using System.Text;
using System.Text.Json;
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
    ///     The attribute used to compact LiteBus headers when the SQS attribute limit would be exceeded.
    /// </summary>
    private const string PackedHeadersAttribute = "litebus-headers";

    /// <summary>
    ///     The strict decoder used to validate SQS message body text.
    /// </summary>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

        var sendRequest = new SendMessageRequest
        {
            QueueUrl = request.Destination,
            MessageBody = useBase64
                ? Convert.ToBase64String(body)
                : Encoding.UTF8.GetString(body)
        };

        ApplyHeaders(sendRequest, request.Headers);

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

        if (!useBase64)
        {
            sendRequest.MessageAttributes.Remove(TransportHeaders.ContentEncoding);
        }

        PackHeadersIfNeeded(sendRequest);
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
        ExpandPackedHeaders(headers);

        return new TransportMessage
        {
            MessagingSystem = TransportMessagingSystems.AmazonSqs,
            Body = body,
            Headers = headers,
            Destination = queueUrl,
            Route = GetAttribute(headers, "Route"),
            MessageId = message.MessageId ?? GetAttribute(headers, TransportHeaders.MessageId),
            CorrelationId = GetAttribute(headers, TransportHeaders.CorrelationId),
            Redelivered = message.Attributes.TryGetValue("ApproximateReceiveCount", out var count) &&
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

        if (message.MessageAttributes.TryGetValue(TransportHeaders.ContentEncoding, out var encoding) &&
            string.Equals(encoding.StringValue, Base64ContentEncoding, StringComparison.Ordinal))
        {
            return Convert.FromBase64String(rawBody);
        }

        return StrictUtf8.GetBytes(rawBody);
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

        string text;

        try
        {
            text = StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var codePoint = char.ConvertToUtf32(text, index);

            if (char.IsHighSurrogate(text[index]))
            {
                index++;
            }

            if (!IsAllowedSqsCodePoint(codePoint))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Returns whether a Unicode scalar is allowed by the SQS message body contract.
    /// </summary>
    /// <param name="codePoint">The Unicode scalar to validate.</param>
    /// <returns><see langword="true" /> when SQS accepts the scalar.</returns>
    private static bool IsAllowedSqsCodePoint(int codePoint)
    {
        return codePoint is 0x9 or 0xA or 0xD ||
               codePoint is >= 0x20 and <= 0xD7FF ||
               codePoint is >= 0xE000 and <= 0xFFFD ||
               codePoint is >= 0x10000 and <= 0x10FFFF;
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
            if (value is null || string.Equals(name, PackedHeadersAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            sendRequest.MessageAttributes[name] = CreateStringAttribute(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>
    ///     Packs non-reserved attributes when the SQS message attribute limit would be exceeded.
    /// </summary>
    /// <param name="sendRequest">The send request whose attributes may be compacted.</param>
    private static void PackHeadersIfNeeded(SendMessageRequest sendRequest)
    {
        if (sendRequest.MessageAttributes.Count <= 10)
        {
            return;
        }

        var packedHeaders = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, value) in sendRequest.MessageAttributes.ToArray())
        {
            if (IsReservedAttribute(name))
            {
                continue;
            }

            sendRequest.MessageAttributes.Remove(name);

            if (value.StringValue is not null)
            {
                packedHeaders[name] = value.StringValue;
            }
        }

        sendRequest.MessageAttributes[PackedHeadersAttribute] = CreateStringAttribute(
            JsonSerializer.Serialize(packedHeaders));
    }

    /// <summary>
    ///     Expands a compacted header attribute while preserving reserved attributes already mapped by SQS.
    /// </summary>
    /// <param name="headers">The headers copied from SQS message attributes.</param>
    private static void ExpandPackedHeaders(Dictionary<string, object?> headers)
    {
        if (!headers.TryGetValue(PackedHeadersAttribute, out var packedValue) ||
            packedValue is not string packedHeaders)
        {
            return;
        }

        headers.Remove(PackedHeadersAttribute);
        var unpackedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(packedHeaders);

        if (unpackedHeaders is null)
        {
            return;
        }

        foreach (var (name, value) in unpackedHeaders)
        {
            if (!headers.ContainsKey(name))
            {
                headers[name] = value;
            }
        }
    }

    /// <summary>
    ///     Returns whether an attribute is written directly by the SQS mapper and must remain outside packed headers.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns><see langword="true" /> for an internal or transport metadata attribute.</returns>
    private static bool IsReservedAttribute(string name)
    {
        return string.Equals(name, PackedHeadersAttribute, StringComparison.Ordinal) ||
               string.Equals(name, TransportHeaders.ContentEncoding, StringComparison.Ordinal) ||
               string.Equals(name, TransportHeaders.MessageId, StringComparison.Ordinal) ||
               string.Equals(name, TransportHeaders.CorrelationId, StringComparison.Ordinal) ||
               string.Equals(name, "Route", StringComparison.Ordinal) ||
               string.Equals(name, "ContentType", StringComparison.Ordinal);
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
    /// <param name="attributes">The SQS message attributes.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static Dictionary<string, object?> CopyMessageAttributes(
        Dictionary<string, MessageAttributeValue> attributes)
    {
        if (attributes.Count == 0)
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
