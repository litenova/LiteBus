namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all message handlers
    /// </summary>
    public interface IMessageHandler<in TMessage, out TMessageResult>
    {
        TMessageResult Handle(TMessage message);
    }
}