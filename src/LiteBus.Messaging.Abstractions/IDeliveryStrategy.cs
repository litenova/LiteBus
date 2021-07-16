namespace LiteBus.Messaging.Abstractions
{
    public interface IDeliveryStrategy<TMessage, TMessageResult>
    {
        TMessageResult Deliver(IMessageContext<TMessage, TMessageResult> messageContext);
    }
}