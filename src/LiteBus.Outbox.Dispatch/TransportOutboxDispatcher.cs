using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.Dispatch;

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
    ///     Gets the optional outbox protector used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the optional tenant routing strategy used to resolve transport routes.
    /// </summary>
    private readonly ITenantRoutingStrategy? _tenantRoutingStrategy;

    /// <summary>
    ///     Gets the transport used as the dispatch target.
    /// </summary>
    private readonly ITransportPublisher _transport;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="transport">The transport used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to validate envelope payloads before publication.</param>
    /// <param name="options">The dispatcher options that control destination settings and routing conventions.</param>
    /// <param name="payloadProtector">The optional outbox protector used to decrypt stored payloads before deserialization.</param>
    /// <param name="tenantRoutingStrategy">The optional tenant routing strategy used to resolve transport routes.</param>
    public TransportOutboxDispatcher(
        ITransportPublisher transport,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TransportOutboxDispatcherOptions options,
        IOutboxPayloadProtector? payloadProtector = null,
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
    /// <remarks>
    ///     The published request carries FIFO addressing: <c>DeduplicationKey</c> is the envelope idempotency key when
    ///     present, otherwise the message identifier, and <c>MessageGroupId</c> is the envelope tenant identifier when
    ///     present, otherwise the contract name.
    /// </remarks>
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);

        var payload = await PayloadProtection.UnprotectAsync(
                message.Payload,
                _payloadProtector,
                new PayloadProtectionContext
                {
                    MessageId = message.Id,
                    ContractName = message.ContractName,
                    ContractVersion = message.ContractVersion,
                    TenantId = message.TenantId,
                    Axis = "outbox"
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (_options.ValidatePayloadBeforeDispatch)
        {
            _ = await _messageSerializer.DeserializeAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
        }

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
                MessageGroupId = string.IsNullOrWhiteSpace(message.TenantId) ? message.ContractName : message.TenantId,
                DeduplicationKey = string.IsNullOrWhiteSpace(message.IdempotencyKey) ? message.Id.ToString("D") : message.IdempotencyKey,
                Headers = OutboxTransportEnvelopeMapper.BuildHeaders(message)
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves the transport route for one outbox envelope.
    /// </summary>
    /// <param name="message">The outbox envelope being dispatched.</param>
    /// <returns>The route passed to the transport publisher.</returns>
    /// <exception cref="LiteBusDispatchException">The configured route resolver returned null or whitespace.</exception>
    /// <remarks>
    ///     The tenant routing strategy wins when registered. Otherwise the configured
    ///     <see cref="TransportOutboxDispatcherOptions.ResolveRoute" /> delegate wins over
    ///     <see cref="OutboxEnvelope.Topic" />, which wins over <see cref="OutboxEnvelope.ContractName" />.
    /// </remarks>
    private string ResolveRoute(OutboxEnvelope message)
    {
        if (_tenantRoutingStrategy is not null)
        {
            return _tenantRoutingStrategy.ResolveRoute(
                message.TenantId,
                message.ContractName,
                message.Topic);
        }

        if (_options.ResolveRoute is not null)
        {
            var resolvedRoute = _options.ResolveRoute(message);
            if (string.IsNullOrWhiteSpace(resolvedRoute))
            {
                throw new LiteBusDispatchException(
                    $"Outbox dispatch failed for message '{message.Id:D}' (contract '{message.ContractName}' version {message.ContractVersion}): " +
                    "the configured route resolver returned null or whitespace. Configure ResolveRoute to return a non-empty route for every envelope.");
            }

            return resolvedRoute;
        }

        if (!string.IsNullOrWhiteSpace(message.Topic))
        {
            return message.Topic;
        }

        return message.ContractName;
    }
}
