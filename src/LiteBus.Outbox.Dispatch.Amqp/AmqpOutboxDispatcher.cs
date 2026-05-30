using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Amqp;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Dispatch.Amqp;

/// <summary>
///     Publishes leased outbox envelopes to an AMQP broker such as RabbitMQ or LavinMQ.
/// </summary>
/// <remarks>
///     <para>
///         The dispatcher validates that the stored contract resolves and deserializes, then publishes the persisted
///         JSON payload without re-serializing the CLR instance. LiteBus envelope metadata is copied to AMQP headers
///         defined in <see cref="AmqpHeaders" />.
///     </para>
///     <para>
///         Publish failures propagate to the outbox processor, which records retry or dead-letter state according to
///         processor options.
///     </para>
/// </remarks>
public sealed class AmqpOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to CLR types.
    /// </summary>
    private readonly IMessageContractRegistry _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to validate envelope payloads before publication.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the dispatcher options that control connection settings and routing conventions.
    /// </summary>
    private readonly AmqpOutboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the AMQP publisher used as the dispatch target.
    /// </summary>
    private readonly IAmqpPublisher _publisher;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="publisher">The AMQP publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to validate envelope payloads before publication.</param>
    /// <param name="options">The dispatcher options that control connection settings and routing conventions.</param>
    public AmqpOutboxDispatcher(
        IAmqpPublisher publisher,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer,
        AmqpOutboxDispatcherOptions options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);
        _ = await _messageSerializer.DeserializeAsync(messageType, message.Payload, cancellationToken).ConfigureAwait(false);

        var routingKey = ResolveRoutingKey(message);
        var body = Encoding.UTF8.GetBytes(message.Payload);

        await _publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = _options.DefaultExchange,
                RoutingKey = routingKey,
                Body = body,
                ContentType = _options.ContentType,
                Persistent = _options.Persistent,
                Mandatory = _options.Mandatory,
                MessageId = message.Id.ToString("D"),
                CorrelationId = message.CorrelationId,
                Headers = BuildHeaders(message)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds LiteBus application headers from the outbox envelope.
    /// </summary>
    /// <param name="message">The outbox envelope whose metadata should be copied to AMQP headers.</param>
    /// <returns>The header dictionary passed to the AMQP publisher.</returns>
    private static Dictionary<string, object?> BuildHeaders(OutboxEnvelope message)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [AmqpHeaders.MessageId] = message.Id.ToString("D"),
            [AmqpHeaders.ContractName] = message.ContractName,
            [AmqpHeaders.ContractVersion] = message.ContractVersion
        };

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            headers[AmqpHeaders.CorrelationId] = message.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(message.CausationId))
        {
            headers[AmqpHeaders.CausationId] = message.CausationId;
        }

        if (!string.IsNullOrWhiteSpace(message.TenantId))
        {
            headers[AmqpHeaders.TenantId] = message.TenantId;
        }

        return headers;
    }

    /// <summary>
    ///     Resolves the AMQP routing key for one outbox envelope.
    /// </summary>
    /// <param name="message">The outbox envelope being dispatched.</param>
    /// <returns>The routing key passed to the AMQP publisher.</returns>
    private string ResolveRoutingKey(OutboxEnvelope message)
    {
        if (!string.IsNullOrWhiteSpace(message.Topic))
        {
            return message.Topic;
        }

        if (_options.ResolveRoutingKey is not null)
        {
            return _options.ResolveRoutingKey(message);
        }

        return message.ContractName;
    }
}
