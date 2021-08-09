namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents a synchronous message handler
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface ISyncMessageHandler<in TMessage> : IMessageHandler<TMessage, VoidMessageResult>
    {
        VoidMessageResult IMessageHandler<TMessage, VoidMessageResult>.Handle(TMessage message, IHandleContext context)
        {
            Handle(message);
            return new VoidMessageResult();
        }

        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>the message result</returns>
        void Handle(TMessage message);
    }

    /// <summary>
    ///     Represents a synchronous message handler
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <typeparam name="TMessageResult">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface ISyncMessageHandler<in TMessage, out TMessageResult> : IMessageHandler<TMessage, TMessageResult>
    {
        TMessageResult IMessageHandler<TMessage, TMessageResult>.Handle(TMessage message, IHandleContext context)
        {
            return Handle(message);
        }

        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <returns>the message result</returns>
        TMessageResult Handle(TMessage message);
    }
}