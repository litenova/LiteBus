namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Serves as a generic base interface for message handlers, allowing for handling operations on messages of specified
///     types and yielding specified result types.
/// </summary>
/// <typeparam name="TMessage">The type parameter representing the type of message to be handled.</typeparam>
/// <typeparam name="TMessageResult">
///     The type parameter representing the type of results produced by the handler after
///     successfully processing a message.
/// </typeparam>
public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    where TMessage : notnull
    where TMessageResult : notnull
{
    /// <summary>
    ///     Processes the specified message and yields a result of type <typeparamref name="TMessageResult" />, facilitating
    ///     targeted and type-safe handling of messages.
    /// </summary>
    /// <param name="message">
    ///     The message of type <typeparamref name="TMessage" /> to be handled, serving as the input for the
    ///     handling operation.
    /// </param>
    /// <returns>
    ///     A result of type <typeparamref name="TMessageResult" /> representing the outcome of the message handling
    ///     process, which might encompass various forms of output such as processed data, transformation results, or potential
    ///     notifications, depending on the specific handling logic implemented.
    /// </returns>
    TMessageResult Handle(TMessage message);
}
