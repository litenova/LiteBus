using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Maps transport deliveries into <see cref="IInbox.AcceptAsync(InboxAcceptItem, System.Threading.CancellationToken)" /> acceptance calls.
/// </summary>
public sealed class TransportInboxIngressHandler
{
    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to CLR types.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the inbox writer used to accept deserialized messages.
    /// </summary>
    private readonly IInbox _inbox;

    /// <summary>
    ///     Gets the mapping policy applied when transport deliveries become acceptance metadata.
    /// </summary>
    private readonly TransportInboxIngressMappingOptions _mappingOptions;

    /// <summary>
    ///     Gets the ingress options that control body limits and delivery authorization.
    /// </summary>
    private readonly TransportInboxIngressOptions _options;

    /// <summary>
    ///     Gets the serializer used to hydrate transport message bodies.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxIngressHandler" /> class.
    /// </summary>
    /// <param name="inbox">The inbox writer used to accept deserialized messages.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate transport message bodies.</param>
    /// <param name="options">The ingress options that control body limits and delivery authorization.</param>
    public TransportInboxIngressHandler(
        IInbox inbox,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        TransportInboxIngressOptions options)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        _inbox = inbox;
        ArgumentNullException.ThrowIfNull(contractRegistry);
        _contractRegistry = contractRegistry;
        ArgumentNullException.ThrowIfNull(messageSerializer);
        _messageSerializer = messageSerializer;
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _mappingOptions = new TransportInboxIngressMappingOptions(
            _options.RequireStableIdentity,
            _options.TrustApplicationHeaders);
    }

    /// <summary>
    ///     Accepts one transport delivery into the inbox store.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization or the store write.</param>
    /// <returns>A task that completes when the inbox accepts the message.</returns>
    public async Task AcceptAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var item = await BuildAcceptItemAsync(message, cancellationToken).ConfigureAwait(false);
            await _inbox.AcceptAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportHeaderMappingException exception)
        {
            throw new InboxIngressException(exception.Message, exception);
        }
    }

    /// <summary>
    ///     Accepts multiple transport deliveries into the inbox store in one batch store round trip.
    /// </summary>
    /// <param name="messages">The received transport deliveries.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization or the store write.</param>
    /// <returns>A task that completes when the inbox accepts every message.</returns>
    public async Task AcceptBatchAsync(
        IReadOnlyList<TransportMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return;
        }

        var items = new InboxAcceptItem[messages.Count];

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];

            items[index] = await BuildAcceptItemAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await _inbox.AcceptBatchAsync(items, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Deserializes one transport delivery and maps its headers to an inbox acceptance item.
    /// </summary>
    /// <param name="message">The received transport delivery.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization.</param>
    /// <returns>An acceptance item ready for <see cref="IInbox.AcceptAsync(InboxAcceptItem, System.Threading.CancellationToken)" />.</returns>
    private async Task<InboxAcceptItem> BuildAcceptItemAsync(
        TransportMessage message,
        CancellationToken cancellationToken)
    {
        if (_options.MaxMessageBytes > 0 && message.Body.Length > _options.MaxMessageBytes)
        {
            throw new InboxIngressException(
                $"Ingress rejected a delivery body of {message.Body.Length} bytes because it exceeds MaxMessageBytes ({_options.MaxMessageBytes}).");
        }

        if (_options.AuthorizeDeliveryAsync is { } authorize)
        {
            await authorize(message, cancellationToken).ConfigureAwait(false);
        }

        var contractName = TransportInboxIngressMapper.GetRequiredHeader(message, TransportHeaders.ContractName);
        var contractVersion = TransportInboxIngressMapper.GetRequiredContractVersion(message);
        var messageType = _contractRegistry.GetMessageType(contractName, contractVersion);
        var payload = Encoding.UTF8.GetString(message.Body.Span);

        var deserialized = await _messageSerializer
            .DeserializeAsync(messageType, payload, cancellationToken)
            .ConfigureAwait(false);

        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(message, _mappingOptions);

        return InboxAcceptItem.From(deserialized, metadata);
    }
}
