using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Transport;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress;

/// <summary>
///     Maps transport deliveries into <see cref="IInbox.AcceptAsync" /> acceptance calls.
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
    ///     Gets the serializer used to hydrate transport message bodies.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportInboxIngressHandler" /> class.
    /// </summary>
    /// <param name="inbox">The inbox writer used to accept deserialized messages.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate transport message bodies.</param>
    public TransportInboxIngressHandler(
        IInbox inbox,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _contractRegistry = contractRegistry ?? throw new ArgumentNullException(nameof(contractRegistry));
        _messageSerializer = messageSerializer ?? throw new ArgumentNullException(nameof(messageSerializer));
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

        using var activity = TransportTracing.StartConsumeActivity(message);

        try
        {
            var contractName = TransportInboxIngressMapper.GetRequiredHeader(message, TransportHeaders.ContractName);
            var contractVersion = TransportInboxIngressMapper.GetRequiredContractVersion(message);
            var messageType = _contractRegistry.GetMessageType(contractName, contractVersion);
            var payload = System.Text.Encoding.UTF8.GetString(message.Body.Span);
            var deserialized = await _messageSerializer
                .DeserializeAsync(messageType, payload, cancellationToken)
                .ConfigureAwait(false);

            var options = TransportInboxIngressMapper.ToInboxOptions(message);
            await _inbox.AcceptAsync(deserialized, messageType, options, cancellationToken).ConfigureAwait(false);
        }
        catch (TransportHeaderMappingException exception)
        {
            throw new InboxDispatchException(exception.Message, exception);
        }
    }

    /// <summary>
    ///     Accepts multiple transport deliveries into the inbox store in one round trip.
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

        try
        {
            var deserializedMessages = new object[messages.Count];
            var messageTypes = new Type[messages.Count];
            var options = new InboxOptions?[messages.Count];

            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                var contractName = TransportInboxIngressMapper.GetRequiredHeader(message, TransportHeaders.ContractName);
                var contractVersion = TransportInboxIngressMapper.GetRequiredContractVersion(message);
                var messageType = _contractRegistry.GetMessageType(contractName, contractVersion);
                var payload = System.Text.Encoding.UTF8.GetString(message.Body.Span);
                var deserialized = await _messageSerializer
                    .DeserializeAsync(messageType, payload, cancellationToken)
                    .ConfigureAwait(false);

                deserializedMessages[index] = deserialized;
                messageTypes[index] = messageType;
                options[index] = TransportInboxIngressMapper.ToInboxOptions(message);
            }

            await _inbox.AcceptBatchAsync(deserializedMessages, messageTypes, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TransportHeaderMappingException exception)
        {
            throw new InboxDispatchException(exception.Message, exception);
        }
    }
}
