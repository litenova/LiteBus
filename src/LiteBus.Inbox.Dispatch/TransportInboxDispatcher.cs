using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Dispatch;

/// <summary>
///     Publishes leased inbox envelopes through a transport-agnostic message transport.
/// </summary>
public sealed class TransportInboxDispatcher : IInboxDispatcher
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
    private readonly TransportInboxDispatcherOptions _options;

    /// <summary>
    ///     Gets the optional inbox protector used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IInboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the optional tenant routing strategy used to resolve transport routes.
    /// </summary>
    private readonly ITenantRoutingStrategy? _tenantRoutingStrategy;

    /// <summary>
    ///     Gets the transport used as the dispatch target.
    /// </summary>
    private readonly ITransportPublisher _transport;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxDispatcher" /> class.
    /// </summary>
    /// <param name="transport">The transport used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to validate envelope payloads before publication.</param>
    /// <param name="options">The dispatcher options that control destination settings and routing conventions.</param>
    /// <param name="payloadProtector">The optional inbox protector used to decrypt stored payloads before deserialization.</param>
    /// <param name="tenantRoutingStrategy">The optional tenant routing strategy used to resolve transport routes.</param>
    public TransportInboxDispatcher(
        ITransportPublisher transport,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TransportInboxDispatcherOptions options,
        IInboxPayloadProtector? payloadProtector = null,
        ITenantRoutingStrategy? tenantRoutingStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        ArgumentNullException.ThrowIfNull(contractRegistry);
        _contractRegistry = contractRegistry;
        ArgumentNullException.ThrowIfNull(messageSerializer);
        _messageSerializer = messageSerializer;
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _payloadProtector = payloadProtector;
        _tenantRoutingStrategy = tenantRoutingStrategy;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var messageType = _contractRegistry.GetMessageType(envelope.ContractName, envelope.ContractVersion);

        var payload = await PayloadProtection.UnprotectAsync(
                envelope.Payload,
                _payloadProtector,
                new PayloadProtectionContext
                {
                    MessageId = envelope.Id,
                    ContractName = envelope.ContractName,
                    ContractVersion = envelope.ContractVersion,
                    TenantId = envelope.TenantId,
                    Axis = "inbox"
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (_options.ValidatePayloadBeforeDispatch)
        {
            _ = await _messageSerializer.DeserializeAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
        }

        var route = ResolveRoute(envelope);
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
                MessageId = envelope.Id.ToString("D"),
                CorrelationId = envelope.CorrelationId,
                Headers = InboxTransportEnvelopeMapper.BuildHeaders(envelope)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves the transport route for one inbox envelope.
    /// </summary>
    /// <param name="envelope">The inbox envelope being dispatched.</param>
    /// <returns>The route passed to the transport publisher.</returns>
    private string ResolveRoute(InboxEnvelope envelope)
    {
        if (_tenantRoutingStrategy is not null)
        {
            return _tenantRoutingStrategy.ResolveRoute(
                envelope.TenantId,
                envelope.ContractName,
                envelope.ContractName);
        }

        if (_options.ResolveRoute is not null)
        {
            return _options.ResolveRoute(envelope);
        }

        return envelope.ContractName;
    }
}
