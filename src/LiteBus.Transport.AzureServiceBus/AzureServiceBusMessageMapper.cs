using System.Globalization;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.AzureServiceBus;

/// <summary>
///     Maps between LiteBus transport messages and Azure Service Bus broker messages.
/// </summary>
internal static class AzureServiceBusMessageMapper
{
    /// <summary>
    ///     Creates a Service Bus message from a LiteBus publish request.
    /// </summary>
    /// <param name="request">The publish request describing destination, body, and headers.</param>
    /// <returns>The broker message ready for send.</returns>
    internal static ServiceBusMessage ToServiceBusMessage(TransportPublishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = new ServiceBusMessage(request.Body)
        {
            ContentType = request.ContentType,
            MessageId = request.MessageId,
            CorrelationId = request.CorrelationId,
            Subject = request.Route
        };

        ApplyHeaders(message, request.Headers);
        return message;
    }

    /// <summary>
    ///     Maps a received Service Bus message to the transport-neutral delivery model.
    /// </summary>
    /// <param name="message">The received broker message.</param>
    /// <param name="destination">The destination name configured for the consumer.</param>
    /// <param name="ackHandlers">The acknowledgement handlers wired by the consumer.</param>
    /// <returns>The transport message passed to consumer handlers.</returns>
    internal static TransportMessage ToTransportMessage(
        ServiceBusReceivedMessage message,
        string destination,
        TransportConsumerAckHandlers ackHandlers)
    {
        ArgumentNullException.ThrowIfNull(ackHandlers);

        return new TransportMessage
        {
            MessagingSystem = TransportMessagingSystems.ServiceBus,
            Body = message.Body,
            Headers = CopyApplicationProperties(message.ApplicationProperties),
            Destination = destination,
            Route = message.Subject,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            Redelivered = message.DeliveryCount > 1,
            AckAsync = ackHandlers.AckAsync,
            NackAsync = ackHandlers.NackAsync
        };
    }

    /// <summary>
    ///     Copies publish headers onto Service Bus application properties.
    /// </summary>
    /// <param name="message">The broker message receiving application properties.</param>
    /// <param name="headers">The optional LiteBus headers.</param>
    private static void ApplyHeaders(ServiceBusMessage message, IReadOnlyDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            message.ApplicationProperties[name] = ConvertHeaderValue(value);
        }
    }

    /// <summary>
    ///     Converts a LiteBus header value into a Service Bus application property value.
    /// </summary>
    /// <param name="value">The header value from the publish request.</param>
    /// <returns>The broker-compatible property value.</returns>
    private static object ConvertHeaderValue(object? value)
    {
        return value switch
        {
            null                        => string.Empty,
            string text                 => text,
            int number                  => number,
            long number                 => number,
            bool flag                   => flag,
            double number               => number,
            float number                => number,
            decimal number              => number,
            DateTimeOffset timestamp    => timestamp.ToString("O", CultureInfo.InvariantCulture),
            DateTime timestamp          => timestamp.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes                => bytes,
            ReadOnlyMemory<byte> memory => memory.ToArray(),
            Memory<byte> memory         => memory.ToArray(),
            _                           => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    /// <summary>
    ///     Copies Service Bus application properties into a read-only header dictionary.
    /// </summary>
    /// <param name="properties">The broker application properties.</param>
    /// <returns>A read-only header dictionary.</returns>
    private static Dictionary<string, object?> CopyApplicationProperties(
        IReadOnlyDictionary<string, object> properties)
    {
        if (properties.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var headers = new Dictionary<string, object?>(properties.Count, StringComparer.Ordinal);

        foreach (var (name, value) in properties)
        {
            headers[name] = value;
        }

        return headers;
    }
}
