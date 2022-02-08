namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a synchronous message handler
/// </summary>
/// <typeparam name="TMessage">the type of message</typeparam>
/// <typeparam name="TMessageResult">the type of message</typeparam>
/// <remarks>The message can be of any type</remarks>
public interface ISyncHandler<in TMessage, out TMessageResult> : IHandler<TMessage, TMessageResult>
{
    TMessageResult IHandler<TMessage, TMessageResult>.Handle(IHandleContext<TMessage> context)
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