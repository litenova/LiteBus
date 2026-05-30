using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Amqp;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Dispatch.Amqp;

/// <summary>
///     Publishes leased inbox envelopes to an AMQP broker such as RabbitMQ or LavinMQ.
/// </summary>
/// <remarks>
///     <para>
///         The dispatcher validates that the stored contract resolves and deserializes, then publishes the persisted
///         JSON payload without re-serializing the CLR instance. LiteBus envelope metadata is copied to AMQP headers
///         defined in <see cref="AmqpHeaders" />.
///     </para>
///     <para>
///         Publish failures propagate to the inbox processor, which records retry or dead-letter state according to
///         processor options.
///     </para>
/// </remarks>
public sealed class AmqpInboxDispatcher : IInboxDispatcher
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
    private readonly AmqpInboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the AMQP publisher used as the dispatch target.
    /// </summary>
    private readonly IAmqpPublisher _publisher;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxDispatcher" /> class.
    /// </summary>
    /// <param name="publisher">The AMQP publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to validate envelope payloads before publication.</param>
    /// <param name="options">The dispatcher options that control connection settings and routing conventions.</param>
    public AmqpInboxDispatcher(
        IAmqpPublisher publisher,
        IMessageContractRegistry contractRegistry,
        IMessageSerializer messageSerializer,
        AmqpInboxDispatcherOptions options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var messageType = _contractRegistry.GetMessageType(envelope.ContractName, envelope.ContractVersion);
        _ = await _messageSerializer.DeserializeAsync(messageType, envelope.Payload, cancellationToken).ConfigureAwait(false);

        var routingKey = ResolveRoutingKey(envelope);
        var body = Encoding.UTF8.GetBytes(envelope.Payload);

        await _publisher.PublishAsync(
            new AmqpPublishRequest
            {
                Exchange = _options.DefaultExchange,
                RoutingKey = routingKey,
                Body = body,
                ContentType = _options.ContentType,
                Persistent = _options.Persistent,
                Mandatory = _options.Mandatory,
                MessageId = envelope.Id.ToString("D"),
                CorrelationId = envelope.CorrelationId,
                Headers = BuildHeaders(envelope)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds LiteBus application headers from the inbox envelope.
    /// </summary>
    /// <param name="envelope">The inbox envelope whose metadata should be copied to AMQP headers.</param>
    /// <returns>The header dictionary passed to the AMQP publisher.</returns>
    private static Dictionary<string, object?> BuildHeaders(InboxEnvelope envelope)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [AmqpHeaders.MessageId] = envelope.Id.ToString("D"),
            [AmqpHeaders.ContractName] = envelope.ContractName,
            [AmqpHeaders.ContractVersion] = envelope.ContractVersion
        };

        if (!string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            headers[AmqpHeaders.CorrelationId] = envelope.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.CausationId))
        {
            headers[AmqpHeaders.CausationId] = envelope.CausationId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.TenantId))
        {
            headers[AmqpHeaders.TenantId] = envelope.TenantId;
        }

        return headers;
    }

    /// <summary>
    ///     Resolves the AMQP routing key for one inbox envelope.
    /// </summary>
    /// <param name="envelope">The inbox envelope being dispatched.</param>
    /// <returns>The routing key passed to the AMQP publisher.</returns>
    private string ResolveRoutingKey(InboxEnvelope envelope)
    {
        if (_options.ResolveRoutingKey is not null)
        {
            return _options.ResolveRoutingKey(envelope);
        }

        return envelope.ContractName;
    }
}
