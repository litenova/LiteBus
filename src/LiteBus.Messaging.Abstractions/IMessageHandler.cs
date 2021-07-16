namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
        object Handle(object message);
    }

    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    {
        object IMessageHandler.Handle(object message)
        {
            return Handle((TMessage) message);
        }

        TMessageResult Handle(TMessage message);
    }
}