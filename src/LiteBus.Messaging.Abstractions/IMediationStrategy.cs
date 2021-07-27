namespace LiteBus.Messaging.Abstractions
{
    public interface IMediationStrategy<TMessage, TMessageResult>
    {
        TMessageResult Mediate(TMessage message, IMessageContext<TMessage, TMessageResult> context);
    }
}