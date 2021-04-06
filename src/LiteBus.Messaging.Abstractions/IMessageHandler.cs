namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
        /// <summary>
        ///     Handles the message
        /// </summary>
        /// <param name="message">The message</param>
        /// <returns>The message result</returns>
        object Handle(IMessage message);
    }

    /// <summary>
    ///     Represents a handler for a message
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <typeparam name="TMessageResult">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler 
        where TMessage : IMessage
    {
        object IMessageHandler.Handle(IMessage message) => Handle((TMessage) message);

        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>the message result</returns>
        TMessageResult Handle(TMessage message);
    }
}