namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageMediationStrategy<TMessage, TMessageResult>
    {
        TMessageResult Mediate(TMessage message, IMessageContext context);
    }
}