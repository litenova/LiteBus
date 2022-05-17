namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a synchronous message handler
/// </summary>
/// <typeparam name="TMessage">the type of message</typeparam>
/// <typeparam name="TMessageResult">the type of message</typeparam>
/// <remarks>The message can be of any type</remarks>
public interface ISyncMessageHandler<in TMessage, out TMessageResult> : IMessageHandler<TMessage, TMessageResult>
{
    TMessageResult IMessageHandler<TMessage, TMessageResult>.Handle(IHandleContext<TMessage> context)
    {
        return Handle(context.Message);
    }

    /// <summary>
    ///     Handles a message
    /// </summary>
    /// <param name="message">the message</param>
    /// <returns>the message result</returns>
    TMessageResult Handle(TMessage message);
}