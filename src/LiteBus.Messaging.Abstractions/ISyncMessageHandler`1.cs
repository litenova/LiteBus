namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a synchronous message handler
/// </summary>
/// <typeparam name="TMessage">the type of message</typeparam>
/// <remarks>The message can be of any type</remarks>
public interface ISyncMessageHandler<in TMessage> : IMessageHandler<TMessage, NoResult>
{
    NoResult IMessageHandler<TMessage, NoResult>.Handle(IHandleContext<TMessage> context)
    {
        Handle(context.Message);
        return new NoResult();
    }

    /// <summary>
    ///     Handles a message
    /// </summary>
    /// <param name="message">the message</param>
    /// <returns>the message result</returns>
    void Handle(TMessage message);
}