using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Inbox.Ingress.Amqp;

/// <summary>
///     Maps AMQP deliveries into <see cref="Abstractions.IInbox.AcceptAsync" /> acceptance calls.
/// </summary>
/// <remarks>
///     Delegates to <see cref="TransportInboxIngressHandler" /> after converting
///     <see cref="AmqpReceivedMessage" /> to <see cref="TransportMessage" />.
/// </remarks>
public sealed class AmqpInboxIngressHandler
{
    /// <summary>
    ///     Gets the transport-neutral ingress handler.
    /// </summary>
    private readonly TransportInboxIngressHandler _inner;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpInboxIngressHandler" /> class.
    /// </summary>
    /// <param name="inbox">The inbox writer used to accept deserialized messages.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to CLR types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate AMQP message bodies.</param>
    public AmqpInboxIngressHandler(
        IInbox inbox,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer)
    {
        _inner = new TransportInboxIngressHandler(inbox, contractRegistry, messageSerializer);
    }

    /// <summary>
    ///     Accepts one AMQP delivery into the inbox store.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization or the store write.</param>
    /// <returns>A task that completes when the inbox accepts the message.</returns>
    public Task AcceptAsync(AmqpReceivedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _inner.AcceptAsync(ToTransportMessage(message), cancellationToken);
    }

    /// <summary>
    ///     Accepts multiple AMQP deliveries into the inbox store in one round trip.
    /// </summary>
    /// <param name="messages">The received AMQP deliveries.</param>
    /// <param name="cancellationToken">The token used to cancel deserialization or the store write.</param>
    /// <returns>A task that completes when the inbox accepts every message.</returns>
    public Task AcceptBatchAsync(
        IReadOnlyList<AmqpReceivedMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (messages.Count == 1)
        {
            return AcceptAsync(messages[0], cancellationToken);
        }

        return _inner.AcceptBatchAsync(messages.Select(ToTransportMessage).ToArray(), cancellationToken);
    }

    /// <summary>
    ///     Converts an AMQP delivery to the transport-neutral message model.
    /// </summary>
    /// <param name="message">The received AMQP delivery.</param>
    /// <returns>The transport message passed to the inner handler.</returns>
    private static TransportMessage ToTransportMessage(AmqpReceivedMessage message)
    {
        return new TransportMessage
        {
            Body = message.Body,
            Headers = message.Headers,
            Destination = message.Exchange,
            Route = message.RoutingKey,
            MessageId = message.MessageId,
            CorrelationId = message.CorrelationId,
            Redelivered = message.Redelivered,
            AckAsync = message.AcceptAsync,
            NackAsync = (requeue, token) => message.NackDelegate(false, requeue, token)
        };
    }
}