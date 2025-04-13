namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Serves as a generic base interface for message handlers, allowing for handling operations on messages of specified types and yielding specified result types.
/// </summary>
/// <typeparam name="TMessage">The type parameter representing the type of message to be handled.</typeparam>
/// <typeparam name="TMessageResult">The type parameter representing the type of results produced by the handler after successfully processing a message.</typeparam>
public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    where TMessage : notnull
    where TMessageResult : notnull
{
    /// <summary>
    /// Provides a non-generic entry point for handling messages, effectively enabling handling of messages of any type, which then get cast to the specified type <typeparamref name="TMessage"/> before being processed by the generic Handle method.
    /// </summary>
    /// <param name="message">The message object to be handled. This object is cast to <typeparamref name="TMessage"/> type before being processed.</param>
    /// <returns>The outcome of the handling operation, derived as a result of processing the cast message through the generic Handle method. The result is represented as an object to maintain a non-generic signature.</returns>
    /// <remarks>
    /// This method operates as a bridge facilitating the handling of messages in a non-generic context, by internally casting messages to the appropriate type and then delegating the processing to the generic Handle method, thus ensuring type safety while providing flexibility.
    /// </remarks>
    object IMessageHandler.Handle(object message)
    {
        return Handle((TMessage) message);
    }

    /// <summary>
    /// Processes the specified message and yields a result of type <typeparamref name="TMessageResult"/>, facilitating targeted and type-safe handling of messages.
    /// </summary>
    /// <param name="message">The message of type <typeparamref name="TMessage"/> to be handled, serving as the input for the handling operation.</param>
    /// <returns>A result of type <typeparamref name="TMessageResult"/> representing the outcome of the message handling process, which might encompass various forms of output such as processed data, transformation results, or potential notifications, depending on the specific handling logic implemented.</returns>
    TMessageResult Handle(TMessage message);
}