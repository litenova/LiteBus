using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.Dispatch.Transport;

/// <summary>
///     Publishes leased outbox envelopes through a transport-agnostic message transport.
/// </summary>
public sealed class TransportOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to CLR types.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the serializer used to validate envelope payloads before publication.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the dispatcher options that control destination settings and routing conventions.
    /// </summary>
    private readonly TransportOutboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the transport used as the dispatch target.
    /// </summary>
    private readonly IMessageTransport _transport;

    /// <summary>
    ///     Gets the optional encryptor used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IPayloadEncryptor? _payloadEncryptor;

    /// <summary>
    ///     Gets the optional tenant routing strategy used to resolve transport routes.
    /// </summary>
    private readonly ITenantRoutingStrategy? _tenantRoutingStrategy;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="transport">The transport used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to validate envelope payloads before publication.</param>
    /// <param name="options">The dispatcher options that control destination settings and routing conventions.</param>
    /// <param name="payloadEncryptor">The optional encryptor used to decrypt stored payloads before deserialization.</param>
    /// <param name="tenantRoutingStrategy">The optional tenant routing strategy used to resolve transport routes.</param>
    public TransportOutboxDispatcher(
        IMessageTransport transport,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TransportOutboxDispatcherOptions options,
        IPayloadEncryptor? payloadEncryptor = null,
        ITenantRoutingStrategy? tenantRoutingStrategy = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _payloadEncryptor = payloadEncryptor;
        _tenantRoutingStrategy = tenantRoutingStrategy;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);
        var payload = await PayloadProtection.UnprotectAsync(message.Payload, _payloadEncryptor, cancellationToken)
            .ConfigureAwait(false);
        _ = await _messageSerializer.DeserializeAsync(messageType, payload, cancellationToken).ConfigureAwait(false);

        var route = ResolveRoute(message);
        var body = Encoding.UTF8.GetBytes(payload);

        await _transport.PublishAsync(
            new TransportPublishRequest
            {
                Destination = _options.DefaultDestination,
                Route = route,
                Body = body,
                ContentType = _options.ContentType,
                Persistent = _options.Persistent,
                Mandatory = _options.Mandatory,
                MessageId = message.Id.ToString("D"),
                CorrelationId = message.CorrelationId,
                Headers = OutboxTransportEnvelopeMapper.BuildHeaders(message)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves the transport route for one outbox envelope.
    /// </summary>
    /// <param name="message">The outbox envelope being dispatched.</param>
    /// <returns>The route passed to the transport publisher.</returns>
    private string ResolveRoute(OutboxEnvelope message)
    {
        if (_tenantRoutingStrategy is not null)
        {
            return _tenantRoutingStrategy.ResolveDestination(
                message.TenantId,
                message.ContractName,
                message.Topic);
        }

        if (!string.IsNullOrWhiteSpace(message.Topic))
        {
            return message.Topic;
        }

        if (_options.ResolveRoute is not null)
        {
            return _options.ResolveRoute(message);
        }

        return message.ContractName;
    }
}
