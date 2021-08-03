namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The non-generic base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
        object Handle(object message);
    }

    /// <summary>
    ///     The generic base of all message handlers
    /// </summary>
    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    {
        object IMessageHandler.Handle(object message) => Handle((TMessage) message);

        new TMessageResult Handle(TMessage message);
    }
}