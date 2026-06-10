using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Maps between LiteBus transport messages and Confluent Kafka records.
/// </summary>
internal static class KafkaMessageMapper
{
    /// <summary>
    ///     Creates a Kafka message from a LiteBus publish request.
    /// </summary>
    /// <param name="request">The publish request describing destination, body, and headers.</param>
    /// <returns>The Kafka message ready for produce.</returns>
    internal static Message<string, byte[]> ToKafkaMessage(TransportPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headers = new Headers();
        ApplyStandardHeaders(headers, request);

        return new Message<string, byte[]>
        {
            Key = request.Route,
            Value = request.Body.ToArray(),
            Headers = headers
        };
    }

    /// <summary>
    ///     Maps a consumed Kafka record to the transport-neutral delivery model.
    /// </summary>
    /// <param name="result">The consumed Kafka record.</param>
    /// <param name="destination">The topic name configured for the consumer.</param>
    /// <param name="commitAsync">The delegate that commits the consumed offset.</param>
    /// <returns>The transport message passed to consumer handlers.</returns>
    /// <remarks>
    ///     <see cref="TransportMessage.NackAsync" /> is intentionally a no-op. Kafka does not rewind committed offsets
    ///     or provide queue-style negative acknowledgement. Uncommitted records are redelivered after consumer restart
    ///     or partition rebalance only.
    /// </remarks>
    internal static TransportMessage ToTransportMessage(
        ConsumeResult<string, byte[]> result,
        string destination,
        Func<CancellationToken, Task> commitAsync) =>
        new()
        {
            Body = result.Message.Value ?? Array.Empty<byte>(),
            Headers = CopyHeaders(result.Message.Headers),
            Destination = destination,
            Route = result.Message.Key,
            MessageId = GetHeader(result.Message.Headers, TransportHeaders.MessageId),
            CorrelationId = GetHeader(result.Message.Headers, TransportHeaders.CorrelationId),
            Redelivered = false,
            AckAsync = commitAsync,
            NackAsync = (_, _) => Task.CompletedTask
        };

    /// <summary>
    ///     Copies standard publish metadata and request headers onto Kafka headers.
    /// </summary>
    /// <param name="headers">The Kafka headers collection.</param>
    /// <param name="request">The publish request supplying metadata and headers.</param>
    private static void ApplyStandardHeaders(Headers headers, TransportPublishRequest request)
    {
        AddHeader(headers, "ContentType", request.ContentType);

        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            AddHeader(headers, TransportHeaders.MessageId, request.MessageId);
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            AddHeader(headers, TransportHeaders.CorrelationId, request.CorrelationId);
        }

        if (request.Headers is null || request.Headers.Count == 0)
        {
            return;
        }

        foreach (var (name, value) in request.Headers)
        {
            if (value is null)
            {
                continue;
            }

            AddHeader(headers, name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>
    ///     Adds one UTF-8 encoded header value to the Kafka headers collection.
    /// </summary>
    /// <param name="headers">The Kafka headers collection.</param>
    /// <param name="name">The header name.</param>
    /// <param name="value">The header value.</param>
    private static void AddHeader(Headers headers, string name, string value) =>
        headers.Add(name, Encoding.UTF8.GetBytes(value));

    /// <summary>
    ///     Copies Kafka headers into a read-only dictionary for handlers.
    /// </summary>
    /// <param name="headers">The Kafka headers from the consumed record.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static IReadOnlyDictionary<string, object?> CopyHeaders(Headers headers)
    {
        if (headers.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var dictionary = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);

        foreach (var header in headers)
        {
            dictionary[header.Key] = header.GetValueBytes() is { } bytes
                ? Encoding.UTF8.GetString(bytes)
                : null;
        }

        return dictionary;
    }

    /// <summary>
    ///     Reads one UTF-8 encoded Kafka header value when present.
    /// </summary>
    /// <param name="headers">The Kafka headers from the consumed record.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The header value, or <see langword="null" /> when absent.</returns>
    private static string? GetHeader(Headers headers, string name)
    {
        foreach (var header in headers)
        {
            if (header.Key == name && header.GetValueBytes() is { } bytes)
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }
}

