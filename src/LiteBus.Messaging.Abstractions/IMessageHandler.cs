namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The non-generic base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
        object Handle(object message, IHandleContext context);
    }

    /// <summary>
    ///     The generic base of all message handlers
    /// </summary>
    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    {
        object IMessageHandler.Handle(object message, IHandleContext context)
        {
            return Handle((TMessage)message, context);
        }

        TMessageResult Handle(TMessage message, IHandleContext context);
    }
}