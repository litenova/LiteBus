using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS.Model;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Aws;

/// <summary>
///     Maps between LiteBus transport messages and Amazon SQS broker messages.
/// </summary>
internal static class SqsMessageMapper
{
    /// <summary>
    ///     Creates an SQS send request from a LiteBus publish request.
    /// </summary>
    /// <param name="request">The publish request describing destination, body, and headers.</param>
    /// <returns>The send request ready for the SQS client.</returns>
    internal static SendMessageRequest ToSendMessageRequest(TransportPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sendRequest = new SendMessageRequest
        {
            QueueUrl = request.Destination,
            MessageBody = Encoding.UTF8.GetString(request.Body.Span)
        };

        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            sendRequest.MessageAttributes[TransportHeaders.MessageId] = CreateStringAttribute(request.MessageId);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            sendRequest.MessageAttributes["CorrelationId"] = CreateStringAttribute(request.CorrelationId);
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
    /// <param name="deleteAsync">The delegate that deletes the message after successful processing.</param>
    /// <param name="changeVisibilityAsync">The delegate that changes message visibility for nack semantics.</param>
    /// <returns>The transport message passed to consumer handlers.</returns>
    internal static TransportMessage ToTransportMessage(
        Message message,
        string queueUrl,
        AwsSqsTransportOptions options,
        Func<CancellationToken, Task> deleteAsync,
        Func<int, CancellationToken, Task> changeVisibilityAsync)
    {
        ArgumentNullException.ThrowIfNull(options);

        var body = Encoding.UTF8.GetBytes(message.Body ?? string.Empty);
        var headers = CopyMessageAttributes(message.MessageAttributes);
        var requeueVisibilityTimeout = SqsRequeueBackoff.ComputeRequeueVisibilityTimeout(message, options);

        return new TransportMessage
        {
            Body = body,
            Headers = headers,
            Destination = queueUrl,
            Route = GetAttribute(headers, "Route"),
            MessageId = message.MessageId ?? GetAttribute(headers, TransportHeaders.MessageId),
            CorrelationId = GetAttribute(headers, TransportHeaders.CorrelationId)
                ?? GetAttribute(headers, "CorrelationId"),
            Redelivered = message.Attributes.TryGetValue("ApproximateReceiveCount", out var count)
                && int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receiveCount)
                && receiveCount > 1,
            AckAsync = deleteAsync,
            NackAsync = (requeue, token) => requeue
                ? changeVisibilityAsync(requeueVisibilityTimeout, token)
                : deleteAsync(token)
        };
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
    private static MessageAttributeValue CreateStringAttribute(string value) =>
        new()
        {
            DataType = "String",
            StringValue = value
        };

    /// <summary>
    ///     Copies SQS message attributes into a read-only header dictionary.
    /// </summary>
    /// <param name="attributes">The SQS message attributes.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static IReadOnlyDictionary<string, object?> CopyMessageAttributes(
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
    private static string? GetAttribute(IReadOnlyDictionary<string, object?> headers, string name)
    {
        return headers.TryGetValue(name, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }
}

