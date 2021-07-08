namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all synchronous message handlers
    /// </summary>
    public interface ISyncMessageHandler : IMessageHandler
    {
        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">The message</param>
        /// <returns>The message result</returns>
        object Handle(object message);
    }

    /// <summary>
    ///     Represents a synchronous message handler
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <typeparam name="TMessageResult">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface ISyncMessageHandler<in TMessage, out TMessageResult> : ISyncMessageHandler where TMessage : IMessage
    {
        object ISyncMessageHandler.Handle(object message) => Handle((TMessage) message);

        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>the message result</returns>
        TMessageResult Handle(TMessage message);
    }
}