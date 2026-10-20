using System;

namespace LiteBus.Transport.Amqp.Exceptions;

/// <summary>
///     Thrown when the broker returns a mandatory AMQP publication as unroutable.
/// </summary>
public sealed class AmqpMessageReturnedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmqpMessageReturnedException" /> class.
    /// </summary>
    /// <param name="message">The error message describing the returned publication.</param>
    /// <param name="exchange">The exchange the returned message was published to.</param>
    /// <param name="routingKey">The routing key used when the message was published.</param>
    /// <param name="replyCode">The AMQP reason code supplied by the broker for the return.</param>
    /// <param name="replyText">The human-readable return reason supplied by the broker.</param>
    /// <param name="innerException">The client exception that reported the return, when there is one.</param>
    public AmqpMessageReturnedException(
        string message,
        string exchange,
        string routingKey,
        ushort replyCode,
        string replyText,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(routingKey);
        ArgumentNullException.ThrowIfNull(replyText);
        Exchange = exchange;
        RoutingKey = routingKey;
        ReplyCode = replyCode;
        ReplyText = replyText;
    }

    /// <summary>
    ///     Gets the exchange the returned message was published to.
    /// </summary>
    public string Exchange { get; }

    /// <summary>
    ///     Gets the routing key used when the message was published.
    /// </summary>
    public string RoutingKey { get; }

    /// <summary>
    ///     Gets the AMQP reason code supplied by the broker for the return.
    /// </summary>
    public ushort ReplyCode { get; }

    /// <summary>
    ///     Gets the human-readable return reason supplied by the broker.
    /// </summary>
    public string ReplyText { get; }
}
